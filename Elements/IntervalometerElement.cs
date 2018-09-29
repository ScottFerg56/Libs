/*
  OOOO             O                                      OOO                              O                    OOOOOOO   OOO                                      O
   OO             OO                                       OO                             OO                     OO  OO    OO                                     OO
   OO             OO                                       OO                             OO                     OO   O    OO                                     OO
   OO   OO OOO  OOOOOO   OOOOO  OO OOO   OO  OO  OOOO      OO    OOOOO  OOO OO   OOOOO  OOOOOO   OOOOO  OO OOO   OO O      OO    OOOOO  OOO OO   OOOOO  OO OOO  OOOOOO
   OO    OOOOOO   OO    OO   OO  OO  OO  OO  OO     OO     OO   OO   OO OOOOOOO OO   OO   OO    OO   OO  OO  OO  OOOO      OO   OO   OO OOOOOOO OO   OO  OOOOOO   OO
   OO    OO  OO   OO    OOOOOOO  OO  OO  OO  OO  OOOOO     OO   OO   OO OO O OO OOOOOOO   OO    OOOOOOO  OO  OO  OO O      OO   OOOOOOO OO O OO OOOOOOO  OO  OO   OO
   OO    OO  OO   OO    OO       OO      OO  OO OO  OO     OO   OO   OO OO O OO OO        OO    OO       OO      OO   O    OO   OO      OO O OO OO       OO  OO   OO
   OO    OO  OO   OO OO OO   OO  OO       OOOO  OO  OO     OO   OO   OO OO O OO OO   OO   OO OO OO   OO  OO      OO  OO    OO   OO   OO OO O OO OO   OO  OO  OO   OO OO
  OOOO   OO  OO    OOO   OOOOO  OOOO       OO    OOO OO   OOOO   OOOOO  OO   OO  OOOOO     OOO   OOOOO  OOOO    OOOOOOO   OOOO   OOOOO  OO   OO  OOOOO   OO  OO    OOO

	(c) 2018 Scott Ferguson
	This code is licensed under MIT license(see LICENSE file for details)
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Elements
{
	/// <summary>
	/// Represents an Intervalometer function on the remote device.
	/// Designed to work with the FMIvalometer Applet in the Arduino mLibs library.
	/// </summary>
	public class IntervalometerElement : RemoteElement
	{
		public IntervalometerElement(IRemoteMaster master, string name, char prefix) : base(master, name, prefix)
		{
		}

		/// <summary>Get/set the delay required between triggering camera focus and tripping the shutter.</summary>
		[ElementProperty('d', readOnly: false, noRequest: true)]
		public uint FocusDelay { get => GetProperty<uint>(); set => SetProperty(value); }

		/// <summary>Get/set the hold time required for the signal tripping the shutter.</summary>
		[ElementProperty('s', readOnly: false, noRequest: true)]
		public uint ShutterHold { get => GetProperty<uint>(); set => SetProperty(value); }

		/// <summary>Get/set the interval time between frames, in milliseconds.</summary>
		[ElementProperty('i', readOnly: false)]
		public uint Interval { get => GetProperty<uint>(); set => SetProperty(value); }

		/// <summary>Get/set the number of frames to be captured for the intervalometer operation.</summary>
		/// <remarks>Setting Frames to a non-zero value will start the intrvalometer operation on the device.</remarks>
		[ElementProperty('f', readOnly: false)]
		public uint Frames { get => GetProperty<uint>(); set => SetProperty(value); }
	}
}
