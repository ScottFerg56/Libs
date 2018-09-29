/*
OOOOOO    OOO                      O               OOO                     OO      OOO
 OO  OO    OO                     OOO               OO                     OO       OO
 OO  OO    OO                    OO OO              OO                              OO
 OO  OO    OO   OO  OO   OOOOO  OO   OO OO OOO    OOOO  OO OOO   OOOOO    OOO     OOOO
 OOOOO     OO   OO  OO  OO   OO OO   OO  OOOOOO  OO OO   OO  OO OO   OO    OO    OO OO
 OO  OO    OO   OO  OO  OOOOOOO OOOOOOO  OO  OO OO  OO   OO  OO OO   OO    OO   OO  OO
 OO  OO    OO   OO  OO  OO      OO   OO  OO  OO OO  OO   OO     OO   OO    OO   OO  OO
 OO  OO    OO   OO  OO  OO   OO OO   OO  OO  OO OO  OO   OO     OO   OO    OO   OO  OO
OOOOOO    OOOO   OOO OO  OOOOO  OO   OO  OO  OO  OOO OO OOOO     OOOOO    OOOO   OOO OO

	(c) 2018 Scott Ferguson
	This code is licensed under MIT license(see LICENSE file for details)
*/

using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Runtime;
using Platforms.BluePortable;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[assembly: Xamarin.Forms.Dependency(typeof(Platforms.BlueAndroid.BlueAndroid))]
namespace Platforms.BlueAndroid
{
	/// <summary>
	/// An Android platform-specific implementation of a Serial Bluetooth LE communications interface.
	/// </summary>
	/// <remarks>
	/// Need to add the following permissions to the .Android\Properties\AndroidManifest.xml:
	///		<uses-permission android:name="android.permission.BLUETOOTH" />
	///		<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" />
	///		<uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
	///
	/// And add a permission check in the OnCreate method in .Android\MainActivity.cs:
	///		if (this.CheckSelfPermission(Manifest.Permission.AccessCoarseLocation) != Permission.Granted)
	///		{
	///			RequestPermissions(new string[] { Manifest.Permission.AccessCoarseLocation }, 0);
	///		}
	/// </remarks>
	public class BlueAndroid : Android.Bluetooth.LE.ScanCallback, IBlueDevice
	{
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
				Debug.WriteLineIf(sw.TraceInfo, $"++> State change: {State}");
				StateChange(this, EventArgs.Empty);
			}
		}

		private string TargetDeviceName;    // The name of the device we're connecting to

		static TraceSwitch sw = new TraceSwitch("BlueAndroid", "BlueAndroid") { Level = TraceLevel.Warning };

		/// <summary>
		/// Fired when the Bluetooth connection State changes.
		/// </summary>
		public event EventHandler StateChange = delegate { };

		/// <summary>
		/// Fired when input is available for reading from the Bluetooth connection.
		/// </summary>
		public event EventHandler InputAvailable = delegate { };

		protected BluetoothAdapter _adapter;
		protected GattCallback _gattCallback;
		protected BluetoothDevice _device;
		protected BluetoothGatt _gatt;
		protected BluetoothGattService _service;
		protected BluetoothGattCharacteristic _TX;
		protected BluetoothGattCharacteristic _RX;

		// IDs required to connect to a Bluetooth LE device for serial communications
		protected Java.Util.UUID uuidService = Java.Util.UUID.FromString("6e400001-b5a3-f393-e0a9-e50e24dcca9e");
		protected Java.Util.UUID uuidTX = Java.Util.UUID.FromString("6e400002-b5a3-f393-e0a9-e50e24dcca9e");
		protected Java.Util.UUID uuidRX = Java.Util.UUID.FromString("6e400003-b5a3-f393-e0a9-e50e24dcca9e");
		protected Java.Util.UUID uuidCharacteristicConfig = Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

		// a list of byte arrays, each of which is from a separate input event
		protected List<byte[]> BytesRead = new List<byte[]>();
		// index into the current (first) byte array of the list for the next byte to be read
		protected int BytesIndex = 0;

		public BlueAndroid()
		{
			var appContext = Android.App.Application.Context;
			if (appContext == null)
				return;
			// get a reference to the bluetooth system service
			var _manager = (BluetoothManager)appContext.GetSystemService("bluetooth");
			if (_manager == null)
				return;
			_adapter = _manager.Adapter;
			_gattCallback = new GattCallback();
			if (_gattCallback == null)
				return;
			_gattCallback.ConnectionStateChange += GattCallback_ConnectionStateChange;
			_gattCallback.ServicesDiscovered += GattCallback_ServicesDiscovered;
			_gattCallback.CharacteristicValueUpdated += GattCallback_CharacteristicValueUpdated;
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
					default:
						return false;
				}
			}
		}

		/// <summary>
		/// Gets an error message associated with the last error.
		/// </summary>
		public string ErrorMessage { get; protected set; }

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
			bool res = _TX.SetValue(data);
			if (!res)
			{
				Debug.WriteLineIf(sw.TraceVerbose, "--> TX SetValue failed");
				return false;
			}
			else
			{
				res = _gatt.WriteCharacteristic(_TX);
				if (!res)
				{
					Debug.WriteLineIf(sw.TraceVerbose, "--> TX WriteCharacteristic failed");
					return false;
				}
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

		/// <summary>
		/// Process a callback from the Gatt to process bytes received from the Bluetooth device.
		/// </summary>
		private void GattCallback_CharacteristicValueUpdated(object sender, CharacteristicReadWriteEventArgs e)
		{
			if (e.Characteristic == _RX)
			{
				var bytes = e.Characteristic.GetValue();
				// add these bytes to the input buffer (as a whole transaction)
				BytesRead.Add(bytes);
				// notify the client
				InputAvailable(this, EventArgs.Empty);
			}
		}

		/// <summary>
		/// Process a callback from the Gatt for device services discovered while we're connecting
		/// </summary>
		private void GattCallback_ServicesDiscovered(object sender, EventArgs e)
		{
			Debug.WriteLineIf(sw.TraceInfo, "++> GattCallback_ServicesDiscovered");
			_service = _gatt.GetService(uuidService);
			_TX = _service.GetCharacteristic(uuidTX);
			_RX = _service.GetCharacteristic(uuidRX);
			BluetoothGattDescriptor config = _RX.GetDescriptor(uuidCharacteristicConfig);
			if (config == null)
			{
				Debug.WriteLineIf(sw.TraceError, "--> _RX.GetDescriptor failed");
				ErrorMessage = "RX.GetDescriptor failed";
				Disconnect();
				return;
			}
			bool b = _gatt.SetCharacteristicNotification(_RX, true);
			b = config.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
			b = _gatt.WriteDescriptor(config);
			// now that services are connected, we're ready to go
			State = BlueState.Connected;
		}

		/// <summary>
		/// Process a callback from the Gatt for changes in the device connection State
		/// </summary>
		private void GattCallback_ConnectionStateChange(object sender, ConnectionStateChangeEventArgs e)
		{
			Debug.WriteLineIf(sw.TraceInfo, $"++> GattCallback_ConnectionStateChange: {e.NewState}");
			switch (e.NewState)
			{
				case ProfileState.Connected:
					_gatt = e.Gatt;
					_service = _gatt.GetService(uuidService);
					_gatt.DiscoverServices();
					// return without setting State
					// wait until ServicesDiscovered callback to announce we're actually connected
					break;
				case ProfileState.Connecting:
					State = BlueState.Connecting;
					break;
				case ProfileState.Disconnected:
					State = BlueState.Disconnected;
					break;
				case ProfileState.Disconnecting:
					State = BlueState.Disconnecting;
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// Connect to a Bluetooth device by name.
		/// </summary>
		/// <param name="name">The name of the device to connect to.</param>
		public async void Connect(string name)
		{
			if (_adapter == null)   // no Bluetooth service (or emulator!)
			{
				ErrorMessage = "No Bluetooth capability";
				Disconnect();
				State = BlueState.NotFound;
				return;
			}

			ErrorMessage = null;
			TargetDeviceName = name;

			// start scanning
			_device = null;
			_adapter.BluetoothLeScanner.StartScan(this);
			State = BlueState.Searching;

			// after an adequate(?) delay, stop the scan
			await Task.Delay(10000);

			if (State == BlueState.Searching)
			{
				Disconnect();
				State = BlueState.NotFound;
			}
		}

		/// <summary>
		/// Disconnect from the device.
		/// </summary>
		public void Disconnect()
		{
			if (State == BlueState.Searching && _adapter != null)
			{
				_adapter.BluetoothLeScanner.FlushPendingScanResults(this);
				_adapter.BluetoothLeScanner.StopScan(this);
			}
			if (_device != null)
			{
				_device = null;
				if (_gatt != null)
				{
					_gatt.Disconnect();
					_gatt = null;
					_service = null;
					_TX = null;
				}
			}
			State = BlueState.Disconnected;
		}

		/// <summary>
		/// Process the discovery of a device during the scan.
		/// </summary>
		/// <param name="callbackType">ScanCallbackType.</param>
		/// <param name="result">The ScanResult containing device information.</param>
		public override void OnScanResult([GeneratedEnum] ScanCallbackType callbackType, ScanResult result)
		{
			switch (callbackType)
			{
				case ScanCallbackType.AllMatches:
					break;
				case ScanCallbackType.FirstMatch:
					break;
				case ScanCallbackType.MatchLost:
					break;
				default:
					break;
			}
			DeviceResult(result);
		}

		/// <summary>
		/// Process the discovery of multiple devices during the scan.
		/// </summary>
		/// <param name="results">The list of ScanResults containing device information.</param>
		public override void OnBatchScanResults(IList<ScanResult> results)
		{
			foreach (var result in results)
			{
				DeviceResult(result);
			}
		}

		/// <summary>
		/// Respond to scan failure.
		/// </summary>
		/// <param name="errorCode"></param>
		public override void OnScanFailed([GeneratedEnum] ScanFailure errorCode)
		{
			//	base.OnScanFailed(errorCode);
			Debug.WriteLineIf(sw.TraceError, $"--> OnScanFailed: {errorCode}");
			ErrorMessage = $"Scan Failed: {errorCode}";
			Disconnect();
			State = BlueState.NotFound;
		}

		/// <summary>
		/// Process a device discovered during the scan.
		/// </summary>
		/// <param name="result">The ScanResult containing device information.</param>
		public void DeviceResult(ScanResult result)
		{
			if (result == null)
				return;
			Debug.WriteLineIf(sw.TraceVerbose, $"++> DeviceResult: {result.Device.Name}");

			if (_device == null && result.Device.Name == TargetDeviceName)
			{
				// found the device we're looking for!
				_device = result.Device;
				// stop the scan
				_adapter.BluetoothLeScanner.FlushPendingScanResults(this);
				_adapter.BluetoothLeScanner.StopScan(this);
				// shout it to the world
				State = BlueState.Found;
				// connecting will spawn further events for setting up the device
				_device.ConnectGatt(Android.App.Application.Context, true, _gattCallback);
			}
		}
	}
}
