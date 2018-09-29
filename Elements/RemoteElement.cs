/*
OOOOOO                             O            OOOOOOO   OOO                                      O
 OO  OO                           OO             OO  OO    OO                                     OO
 OO  OO                           OO             OO   O    OO                                     OO
 OO  OO  OOOOO  OOO OO   OOOOO  OOOOOO   OOOOO   OO O      OO    OOOOO  OOO OO   OOOOO  OO OOO  OOOOOO
 OOOOO  OO   OO OOOOOOO OO   OO   OO    OO   OO  OOOO      OO   OO   OO OOOOOOO OO   OO  OOOOOO   OO
 OO OO  OOOOOOO OO O OO OO   OO   OO    OOOOOOO  OO O      OO   OOOOOOO OO O OO OOOOOOO  OO  OO   OO
 OO  OO OO      OO O OO OO   OO   OO    OO       OO   O    OO   OO      OO O OO OO       OO  OO   OO
 OO  OO OO   OO OO O OO OO   OO   OO OO OO   OO  OO  OO    OO   OO   OO OO O OO OO   OO  OO  OO   OO OO
OOO  OO  OOOOO  OO   OO  OOOOO     OOO   OOOOO  OOOOOOO   OOOO   OOOOO  OO   OO  OOOOO   OO  OO    OOO

	(c) 2018 Scott Ferguson
	This code is licensed under MIT license(see LICENSE file for details)
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Elements
{
	/// <summary>
	/// Identifies a property to be supported by a RemoteElement.
	/// </summary>
	[AttributeUsage(AttributeTargets.All)]
	public class ElementPropertyAttribute : Attribute
	{
		/// <summary>
		/// The prefix character used to indicate the property in communications.
		/// </summary>
		public char Prefix { get; protected set; }

		/// <summary>
		/// Marks a property as not to be sent to the remote device.
		/// </summary>
		public bool ReadOnly { get; protected set; }

		/// <summary>
		/// Marks a property as not to be requested from the remote device.
		/// </summary>
		public bool NoRequest { get; protected set; }

		/// <summary>
		/// Marks a property to write to the remote device even if local value appears unchanged.
		/// </summary>
		public bool ForceWrite { get; protected set; }

		/// <summary>
		/// Initial value for the property.
		/// </summary>
		public object InitialValue { get; protected set; }

		/// <summary>
		/// Construct an ElementPropertyAttribute.
		/// </summary>
		/// <param name="name"></param>
		public ElementPropertyAttribute(char prefix, bool readOnly = false, object initialValue = null, bool noRequest = false, bool forceWrite = false)
		{ Prefix = prefix; ReadOnly = readOnly; InitialValue = initialValue; NoRequest = noRequest; ForceWrite = forceWrite; }
	}

	/// <summary>
	/// A definition for how we interact with the properties of a RemoteElement.
	/// </summary>
	public class PropertyDef
	{
		public string Name;			// The name of the property.
		public Type PropertyType;	// The type of the property.
		public char Prefix;         // The prefix character used to indicate the property in communications.
		public bool ReadOnly;       // Marks a property as not to be sent to the remote device.
		public bool NoRequest;      // Marks a property as not to be requested from the remote device.
		public bool ForceWrite;     // Marks a property to write to the remote device even if local value appears unchanged.
		public object Value;		// The property Value.
		public bool Requested;		// Flag when we've requested a property to avoid duplicate requests.

		public PropertyDef(PropertyDescriptor desc, ElementPropertyAttribute attr)
		{
			Name = desc.Name;
			PropertyType = desc.PropertyType;
			Prefix = attr.Prefix;
			ReadOnly = attr.ReadOnly;
			NoRequest = attr.NoRequest;
			ForceWrite = attr.ForceWrite;
			Value = attr.InitialValue;
		}

		/// <summary>
		/// Set the property value from a string from device communications.
		/// </summary>
		/// <param name="s">The string representing the new Value.</param>
		/// <returns>True if the value was successfully parsed and set.</returns>
		public bool SetStringValue(string s)
		{
			if (PropertyType.IsEnum)
			{
				try
				{
					Value = Enum.Parse(PropertyType, s, true);
					return true;
				}
				catch (Exception)
				{
					Debug.WriteLine($"--> Failed to parse input Enum: {s}");
					return false;
				}
			}
			switch (Type.GetTypeCode(PropertyType))
			{
				case TypeCode.Boolean:
					Value = s[0] == '1' || s[0] == 't';
					break;
				case TypeCode.Int16:
				case TypeCode.Int32:
				case TypeCode.Int64:
				case TypeCode.SByte:
				case TypeCode.UInt16:
				case TypeCode.UInt32:
				case TypeCode.UInt64:
				case TypeCode.Byte:
				case TypeCode.Double:
				case TypeCode.Single:
					{
						if (!double.TryParse(s, out double v))
							return false;
						Value = Convert.ChangeType(v, PropertyType);
					}
					break;
				case TypeCode.Char:
					Value = s[0];
					break;
				case TypeCode.String:
					Value = s;
					break;
				case TypeCode.DateTime:
				case TypeCode.DBNull:
				default:
					Debug.WriteLine($"--> Unsupported property type for {Name}: {PropertyType.Name}");
					return false;
			}
			return true;
		}

		/// <summary>
		/// Get the property value represented as a string for device communications.
		/// </summary>
		/// <returns>The string representation of the value.</returns>
		public string GetStringValue()
		{
			if (Value == null)
				return "";
			if (PropertyType.IsEnum)
			{
				return ((int)Value).ToString();
			}
			switch (Type.GetTypeCode(PropertyType))
			{
				case TypeCode.Boolean:
					return (bool)Value ? "1" : "0";
				case TypeCode.Int16:
				case TypeCode.Int32:
				case TypeCode.Int64:
				case TypeCode.SByte:
				case TypeCode.UInt16:
				case TypeCode.UInt32:
				case TypeCode.UInt64:
				case TypeCode.Byte:
					return Value.ToString();
				case TypeCode.Double:
				case TypeCode.Single:
					return $"{((double)Value):0.##}";
				case TypeCode.Char:
					return new string((char)Value, 1);
				case TypeCode.String:
					return (string)Value;
				case TypeCode.DateTime:
				case TypeCode.DBNull:
				default:
					Debug.WriteLine($"--> Unsupported property type for {Name}: {PropertyType.Name}");
					return "";
			}
		}
	};

	/// <summary>
	/// An interface required by the manager of RemoteElements.
	/// </summary>
	public interface IRemoteMaster
	{
		void Output(string data, bool required = true);
	}

	/// <summary>
	/// Represents a remote object communicating via property exchange.
	/// Designed to work with Applets in the Arduino mLibs library.
	/// </summary>
	public abstract class RemoteElement : INotifyPropertyChanged
	{
		IRemoteMaster Master;
		public readonly string Name;             // friendly name for the element
		public readonly char Prefix;   // character prefix used in interface command strings
		protected List<PropertyDef> Properties;

		protected RemoteElement(IRemoteMaster master, string name, char prefix)
		{
			Master = master;
			Name = name;
			Prefix = prefix;

			// Properties marked with the ElementPropertyAttribute by the extender of this class
			// are added to our list.
			Properties = new List<PropertyDef>();
			foreach (PropertyDescriptor desc in TypeDescriptor.GetProperties(this))
			{
				var attr = desc.Attributes.OfType<ElementPropertyAttribute>().FirstOrDefault();
				if (attr == null)
					continue;
				var prop = new PropertyDef(desc, attr);
				Properties.Add(prop);
			}
		}

		/// <summary>
		/// Process an input string.
		/// </summary>
		/// <param name="s">The input string.</param>
		/// <remarks>
		/// The first character of the string indicates a property to be accessed or an action.
		/// The remainder of the string will be a value for the property.
		/// </remarks>
		public void Input(string s)
		{
			if (s.Length < 2)
				return;

			if (s[0] != '=')
			{
				Debug.WriteLine($"--> Unrecognized input command: {s}");
				return;
			}
			var prop = Properties.FirstOrDefault(p => p.Prefix == s[1]);
			if (prop == null)
			{
				Debug.WriteLine($"--> Unrecognized property: {s}");
				return;
			}

			// keeping track of properties we've requested values for, to avoid redundant requests
			prop.Requested = false;
			prop.SetStringValue(s.Substring(2));

		//	Debug.WriteLine($"++> {Name} {prop.Name} <- {prop.GetStringValue()}");
			OnPropertyChanged(prop.Name);
		}

		/// <summary>
		/// Send a command string to the device.
		/// </summary>
		/// <param name="cmd">The command string.</param>
		/// <param name="required">False for non-essential data that can be skipped.</param>
		protected void Output(string cmd, bool required = true)
		{
			Master.Output($"{Prefix}{cmd}", required);
		}

		/// <summary>
		/// Send a property value to the device.
		/// </summary>
		/// <param name="prop">The property to be set.</param>
		/// <param name="v">The value to be set.</param>
		protected void SetDeviceProp(PropertyDef prop, bool required = true)
		{
		//	Debug.WriteLine($"++> {Name} {prop.Name} -> {prop.GetStringValue()}");
			// the '=' preceding the property code assigns the property value
			Output($"={prop.Prefix}{prop.GetStringValue()}", required);
		}

		/// <summary>
		/// Request a property value from the device.
		/// </summary>
		/// <param name="prop">The property to request.</param>
		protected void RequestDeviceProp(PropertyDef prop)
		{
			if (prop.NoRequest)			// we've been asked to skip these
				return;
			if (prop.Requested)			// skip a redundant request
				return;
			prop.Requested = true;		// note the request has been made
			// the '?' preceding the property code requests the property value
			Output($"?{prop.Prefix}");
		}

		/// <summary>
		/// Request all properties from the device.
		/// </summary>
		public void RequestAllDeviceProps()
		{
			// build a single request packet for all applicable properties at once
			string p = "?";
			foreach (var prop in Properties)
			{
				if (!prop.NoRequest)
				{
					prop.Requested = true;     // force request
					p += prop.Prefix;
				}
			}
			Output(p);
		}

		/// <summary>
		/// Property getter for the element.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="propertyName">The name of the property to access.</param>
		/// <returns>The value of the property.</returns>
		/// <remarks>Defers property access to the Sequence.</remarks>
		protected T GetProperty<T>([CallerMemberName]string propertyName = "")
		{
			var prop = Properties.FirstOrDefault(p => p.Name == propertyName);
			Debug.Assert(prop != null, $"Property not found: {propertyName}");
			if (prop.Value == null)
			{
				RequestDeviceProp(prop);
				return default(T);
			}
			var v = Convert.ChangeType(prop.Value, prop.PropertyType);
			return (T)v;
		}

		/// <summary>
		/// Property setter for the element.
		/// </summary>
		/// <typeparam name="T">The type of the property.</typeparam>
		/// <param name="value">The value to be assigned.</param>
		/// <param name="propertyName">The name of the property.</param>
		/// <param name="onChanged">An Action to be invoked when a change is detected.</param>
		/// <returns>True if the value changed.</returns>
		protected bool SetProperty<T>(T value, [CallerMemberName]string propertyName = "", Func<object, bool> setCondition = null, Action onChanged = null)
		{
			var prop = Properties.FirstOrDefault(p => p.Name == propertyName);
			Debug.Assert(prop != null, $"Property not found: {propertyName}");
			if (prop.Value != null && !prop.ForceWrite)
			{
				var v = (T)Convert.ChangeType(prop.Value, prop.PropertyType);
				if (EqualityComparer<T>.Default.Equals(v, value))
					return false;
			}

			prop.Value = value;
			if (!prop.ReadOnly)
			{
				SetDeviceProp(prop, setCondition == null || setCondition(prop.Value));
			}
			onChanged?.Invoke();
			OnPropertyChanged(propertyName);
			return true;
		}

		#region INotifyPropertyChanged
		/// <summary>
		/// Fired when a property value changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Fire an event for a property changing.
		/// </summary>
		/// <param name="propertyName">The name of the changed property.</param>
		protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
		#endregion
	}
}
