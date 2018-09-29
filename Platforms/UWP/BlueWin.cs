/*
OOOOOO    OOO                   OO   OO    OO
 OO  OO    OO                   OO   OO    OO
 OO  OO    OO                   OO   OO
 OO  OO    OO   OO  OO   OOOOO  OO   OO   OOO   OO OOO
 OOOOO     OO   OO  OO  OO   OO OO O OO    OO    OOOOOO
 OO  OO    OO   OO  OO  OOOOOOO OO O OO    OO    OO  OO
 OO  OO    OO   OO  OO  OO      OOOOOOO    OO    OO  OO
 OO  OO    OO   OO  OO  OO   OO  OOOOO     OO    OO  OO
OOOOOO    OOOO   OOO OO  OOOOO   OO OO    OOOO   OO  OO

	(c) 2018 Scott Ferguson
	This code is licensed under MIT license(see LICENSE file for details)
*/

using Platforms.BluePortable;
using CamSlider;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

[assembly: Xamarin.Forms.Dependency(typeof(Platforms.BlueWin.BlueWin))]
namespace Platforms.BlueWin
{
	/// <summary>
	/// A Windows UWP platform-specific implementation of a Serial Bluetooth LE communications interface.
	/// </summary>
	/// <remarks>
	/// Need to check 'Bluetooth' in the Package.appxmanifest for UWP.
	/// </remarks>
	public class BlueWin : IBlueDevice
	{
		static TraceSwitch sw = new TraceSwitch("BlueWin", "BlueWin") { Level = TraceLevel.Warning };

		public BlueState _State = BlueState.Disconnected;
		/// <summary>
		/// Gets the connection state.
		/// </summary>
		public BlueState State
		{
			get { return _State; }
			protected set
			{
				if (_State == value)
					return;
				_State = value;
				Debug.WriteLineIf(sw.TraceVerbose, $"++> State change: {State}");
				StateChange(this, EventArgs.Empty);
			}
		}

		private string TargetDeviceName;    // The name of the device we're connecting to

		/// <summary>
		/// Fired when the Bluetooth connection State changes.
		/// </summary>
		public event EventHandler StateChange = delegate { };

		/// <summary>
		/// Fired when input is available for reading from the Bluetooth connection.
		/// </summary>
		public event EventHandler InputAvailable = delegate { };

		private DeviceWatcher deviceWatcher;
		private DeviceInformation DeviceInfo;
		private BluetoothLEDevice bluetoothLeDevice;
		private GattDeviceService Service;
		private GattCharacteristic _TX;
		private GattCharacteristic _RX;

		// IDs required to connect to a Bluetooth LE device for serial communications
		private readonly Guid uuidService = Guid.Parse("6e400001-b5a3-f393-e0a9-e50e24dcca9e");
		private readonly Guid uuidTX = Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
		private readonly Guid uuidRX = Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e");
		private readonly Guid uuidCharacteristicConfig = Guid.Parse("00002902-0000-1000-8000-00805f9b34fb");

		// a list of byte arrays, each of which is from a separate input event
		protected List<byte[]> BytesRead = new List<byte[]>();
		// index into the current (first) byte array of the list for the next byte to be read
		protected int BytesIndex = 0;

		public BlueWin()
		{
		}

		/// <summary>
		/// True if the device is in a state where connection is possible.
		/// </summary>
		public bool CanConnect
		{
			get
			{
				switch (State)
				{
					case BlueState.Disconnected:
					case BlueState.Disconnecting:
					case BlueState.NotFound:
						return true;
					case BlueState.Connected:
					case BlueState.Connecting:
					case BlueState.Searching:
					case BlueState.Found:
						return false;
					default:
						Debug.WriteLineIf(sw.TraceError, "--> Unknown State value");
						return false;
				}
			}
		}

		/// <summary>
		/// Gets an error message associated with the last error.
		/// </summary>
		public string ErrorMessage { get; protected set; }

		/// <summary>
		/// Start the scan for our target Bluetooth device.
		/// </summary>
		private void StartBleDeviceWatcher()
		{
			if (deviceWatcher == null)
			{
				// Additional properties we would like about the device.
				// Property strings are documented here https://msdn.microsoft.com/en-us/library/windows/desktop/ff521659(v=vs.85).aspx
				string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };

				// requesting paired and non-paired in a single query
				string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

				deviceWatcher =
						DeviceInformation.CreateWatcher(
							aqsAllBluetoothLEDevices,
							requestedProperties,
							DeviceInformationKind.AssociationEndpoint);

				// Register event handlers before starting the watcher.
				deviceWatcher.Added += DeviceWatcher_Added;
				deviceWatcher.Updated += DeviceWatcher_Updated;
				deviceWatcher.Removed += DeviceWatcher_Removed;
				deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
				deviceWatcher.Stopped += DeviceWatcher_EnumerationCompleted;
			}
			State = BlueState.Searching;
			// Start the watcher.
			deviceWatcher.Start();
		}

		/// <summary>
		/// Stop the scan for our target Bluetooth device.
		/// </summary>
		private void StopBleDeviceWatcher()
		{
			if (deviceWatcher != null)
			{
				if (deviceWatcher.Status == DeviceWatcherStatus.Started)
				{
					// Stop the watcher.
					deviceWatcher.Stop();
				}
				deviceWatcher = null;
			}
		}

		/// <summary>
		/// Process a device discovered during the scan.
		/// </summary>
		/// <param name="sender">The DeviceWatcher that found the device.</param>
		/// <param name="deviceInfo">The DeviceInformation for the discovered device.</param>
		private async void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
		{
			// Protect against race condition if the task runs after the app stopped the deviceWatcher.
			if (sender == deviceWatcher)
			{
				Debug.WriteLineIf(sw.TraceVerbose, "Device found: {0}", deviceInfo.Name);
				// check for our target device
				if (DeviceInfo == null && deviceInfo.Name == TargetDeviceName)
				{
					// found the device we're looking for!
					DeviceInfo = deviceInfo;
					// stop the scan
					StopBleDeviceWatcher();
					// shout it to the world
					State = BlueState.Found;
					// setup the device
					await SetupDevice();
				}
			}
		}

		/// <summary>
		/// Setup and connect to services for the discovered device.
		/// </summary>
		async Task SetupDevice()
		{
			State = BlueState.Connecting;

			try
			{
				Debug.WriteLineIf(sw.TraceVerbose, "++> Getting Bluetooth LE device");
				// BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.
				bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(DeviceInfo.Id);
			}
			catch (Exception ex) when ((uint)ex.HResult == 0x800710df)
			{
				ErrorMessage = "ERROR_DEVICE_NOT_AVAILABLE because the Bluetooth radio is not on.";
			}
			catch (Exception ex)
			{
				ErrorMessage = "FromIdAsync ERROR: " + ex.Message;
			}

			if (bluetoothLeDevice == null)
			{
				Disconnect();
				return;
			}

			var sr = await bluetoothLeDevice.GetGattServicesForUuidAsync(uuidService, BluetoothCacheMode.Uncached);
			if (sr.Status != GattCommunicationStatus.Success || !sr.Services.Any())
			{
				ErrorMessage = "Can't find service: " + sr.Status.ToString();
				Disconnect();
				return;
			}
			Service = sr.Services.First();
			Debug.WriteLineIf(sw.TraceVerbose, "++> GattService found");

			var accessStatus = await Service.RequestAccessAsync();
			if (accessStatus != DeviceAccessStatus.Allowed)
			{
				// Not granted access
				ErrorMessage = "Error accessing service:" + accessStatus.ToString();
				Disconnect();
				return;
			}
			var charResult = await Service.GetCharacteristicsForUuidAsync(uuidTX, BluetoothCacheMode.Uncached);
			if (charResult.Status != GattCommunicationStatus.Success || !charResult.Characteristics.Any())
			{
				ErrorMessage = "Error getting TX characteristic: " + charResult.Status.ToString();
				Disconnect();
				return;
			}
			_TX = charResult.Characteristics.First();
			charResult = await Service.GetCharacteristicsForUuidAsync(uuidRX, BluetoothCacheMode.Uncached);
			if (charResult.Status != GattCommunicationStatus.Success || !charResult.Characteristics.Any())
			{
				ErrorMessage = "Error getting RX characteristic: " + charResult.Status.ToString();
				Disconnect();
				return;
			}
			_RX = charResult.Characteristics.First();
			try
			{
				Debug.WriteLineIf(sw.TraceVerbose, "++> Setting Notify descriptor");
				var res = await _RX.WriteClientCharacteristicConfigurationDescriptorAsync(
										GattClientCharacteristicConfigurationDescriptorValue.Notify);
				if (res != GattCommunicationStatus.Success)
				{
					ErrorMessage = "Error setting RX notify: " + charResult.Status.ToString();
					Disconnect();
					return;
				}
			}
			catch (Exception ex)
			{
				ErrorMessage = "Exception setting RX notify: " + ex.Message;
				Disconnect();
				return;
			}
			_RX.ValueChanged += Receive_ValueChanged;
			bluetoothLeDevice.ConnectionStatusChanged += BluetoothLeDevice_ConnectionStatusChanged;
			// now that services are connected, we're ready to go
			State = BlueState.Connected;
		}

		/// <summary>
		/// Process changes in the device connection State
		/// </summary>
		private void BluetoothLeDevice_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
		{
			switch (bluetoothLeDevice.ConnectionStatus)
			{
				case BluetoothConnectionStatus.Disconnected:
					Disconnect();
					break;
				case BluetoothConnectionStatus.Connected:
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Process bytes received from the Bluetooth device.
		/// </summary>
		private void Receive_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
		{
			Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);
			// add these bytes to the input buffer (as a whole transaction)
			BytesRead.Add(data);
			// notify the client
			InputAvailable(this, EventArgs.Empty);
		}

		/// <summary>
		/// Respond to events during scan about an updated device.
		/// </summary>
		private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
		{
			// Protect against race condition if the task runs after the app stopped the deviceWatcher.
			if (sender == deviceWatcher)
			{
				if (DeviceInfo != null && DeviceInfo.Id == deviceInfoUpdate.Id)
					DeviceInfo.Update(deviceInfoUpdate);
			}
		}

		/// <summary>
		/// Respond to events during scan about a removed device.
		/// </summary>
		private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate deviceInfoUpdate)
		{
			// Protect against race condition if the task runs after the app stopped the deviceWatcher.
			if (sender == deviceWatcher)
			{
				if (DeviceInfo != null && DeviceInfo.Id == deviceInfoUpdate.Id)
					DeviceInfo = null;
			}
		}

		/// <summary>
		/// Process notification that the scan is complete.
		/// </summary>
		private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object e)
		{
			// Protect against race condition if the task runs after the app stopped the deviceWatcher.
			if (sender == deviceWatcher)
			{
				if (DeviceInfo == null)
				{
					// never found it
					StopBleDeviceWatcher();
					State = BlueState.NotFound;
				}
				else
				{
					// should have already raised Found event
				}
			}
		}

		/// <summary>
		/// Connect to a Bluetooth device by name.
		/// </summary>
		/// <param name="name">The name of the device to connect to.</param>
		public void Connect(string name)
		{
			ErrorMessage = null;
			TargetDeviceName = name;
			StartBleDeviceWatcher();
		}

		/// <summary>
		/// Disconnect from the device.
		/// </summary>
		public void Disconnect()
		{
			if (CanConnect)
				return;
			StopBleDeviceWatcher();
			State = BlueState.Disconnecting;
			Cleanup();
		}

		/// <summary>
		/// Cleanup after disconnect.
		/// </summary>
		void Cleanup()
		{
			DeviceInfo = null;
			if (_RX != null)
			{
				_RX.ValueChanged -= Receive_ValueChanged;
				_RX = null;
			}
			_TX = null;
			if (Service != null)
			{
				Service.Dispose();
				Service = null;
			}
			if (bluetoothLeDevice != null)
			{
				bluetoothLeDevice.ConnectionStatusChanged -= BluetoothLeDevice_ConnectionStatusChanged;
				bluetoothLeDevice.Dispose();
				bluetoothLeDevice = null;
			}
			State = BlueState.Disconnected;
		}

		/// <summary>
		/// True if there is a byte available to be read.
		/// </summary>
		public bool ByteAvailable => BytesRead.Count != 0;

		/// <summary>
		/// Get the next byte from the Bluetooth device.
		/// </summary>
		/// <returns>The next byte that has already been received.</returns>
		/// <remarks>There must be a ByteAvailable for this to succeed.</remarks>
		public byte GetByte()
		{
			// get a byte from the first array inthe list and advance the index
			byte b = BytesRead[0][BytesIndex++];
			if (BytesIndex >= BytesRead[0].Length)
			{
				// just used the last byte in the current array, remove it from the list
				BytesRead.RemoveAt(0);
				BytesIndex = 0;
			}
			return b;
		}

		/// <summary>
		/// Write byte data to the Bluetooth device.
		/// </summary>
		/// <param name="data">The array of bytes to write.</param>
		/// <returns>True if the write succeeded.</returns>
		public bool Write(params byte[] data)
		{
			if (State != BlueState.Connected)
				return false;
			if (_TX == null)
			{
				Debug.WriteLineIf(sw.TraceError, $"--> Write no TX characteristic");
				return false;
			}
		//	var writeBuffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(data);
			var writeBuffer = data.AsBuffer();
			try
			{
				// Writes the value from the buffer to the characteristic.
				var result = _TX.WriteValueAsync(writeBuffer);
				result.AsTask().Wait();

				if (result.GetResults() == GattCommunicationStatus.Success)
				{
					    Debug.WriteLineIf(sw.TraceVerbose, "Successfully wrote value to device");
				}
				else
				{
					Debug.WriteLineIf(sw.TraceError, $"--> Write failed: {result.GetResults()}");
					return false;
				}
			}
			
			catch (Exception ex) when ((uint)ex.HResult == 0x80131500)
			{
				Debug.WriteLineIf(sw.TraceError, $"--> Unexpected disconnection: {ex.Message}");
				Disconnect();
				return false;
			}
			catch (Exception ex) when ((uint)ex.HResult == 0x80650003 || (uint)ex.HResult == 0x80070005)
			{
				// E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED or E_ACCESSDENIED
				// This usually happens when a device reports that it support writing, but it actually doesn't.
				Debug.WriteLineIf(sw.TraceError, $"--> Write failure: {ex.Message}");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Write a string to the Bluetooth device.
		/// </summary>
		/// <param name="data">The string to write.</param>
		public bool Write(string data)
		{
			return Write(Encoding.UTF8.GetBytes(data));
		}
	}
}
