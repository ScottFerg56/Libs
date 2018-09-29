/*
 OOOOO     O                                            OOOOOOO   OOO                                      O
OO   OO   OO                                             OO  OO    OO                                     OO
OO   OO   OO                                             OO   O    OO                                     OO
 OO     OOOOOO   OOOOO  OO OOO  OO OOO   OOOOO  OO OOO   OO O      OO    OOOOO  OOO OO   OOOOO  OO OOO  OOOOOO
  OOO     OO    OO   OO  OO  OO  OO  OO OO   OO  OO  OO  OOOO      OO   OO   OO OOOOOOO OO   OO  OOOOOO   OO
    OO    OO    OOOOOOO  OO  OO  OO  OO OOOOOOO  OO  OO  OO O      OO   OOOOOOO OO O OO OOOOOOO  OO  OO   OO
OO   OO   OO    OO       OO  OO  OO  OO OO       OO      OO   O    OO   OO      OO O OO OO       OO  OO   OO
OO   OO   OO OO OO   OO  OOOOO   OOOOO  OO   OO  OO      OO  OO    OO   OO   OO OO O OO OO   OO  OO  OO   OO OO
 OOOOO     OOO   OOOOO   OO      OO      OOOOO  OOOO    OOOOOOO   OOOO   OOOOO  OO   OO  OOOOO   OO  OO    OOO
                         OO      OO
                        OOOO    OOOO

	(c) 2018 Scott Ferguson
	This code is licensed under MIT license(see LICENSE file for details)
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Elements
{
	/// <summary>
	/// Represents a stepper used to control functions on the remote device.
	/// Designed to work with the FMStepper Applet in the Arduino mLibs library.
	/// </summary>
	public class StepperElement : RemoteElement
	{
		public StepperElement(IRemoteMaster master, string name, char prefix) : base(master, name, prefix)
		{
		}

		/// <summary>Gets the Position of the stepper.</summary>
		[ElementProperty('p', readOnly: false)]
		public double Position { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>Get/set the Target Position the stepper is moving toward.</summary>
		[ElementProperty('t', readOnly: false, forceWrite: true)]
		public double TargetPosition { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>Get/set the Max Limit of the range for Position values.</summary>
		[ElementProperty('x', readOnly: false, initialValue: double.MaxValue)]
		public double MaxLimit { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>Get/set the Min Limit of the range for Position values.</summary>
		[ElementProperty('n', readOnly: false, initialValue: double.MaxValue)]
		public double MinLimit { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>Get/set the Acceleration used by the stepper.</summary>
		[ElementProperty('a', readOnly: false)]
		public double Acceleration { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>Get/set the Max Speed the stepper will accelerate to for movements.</summary>
		[ElementProperty('m', readOnly: false)]
		public double MaxSpeed { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>Get the upper limit for MaxSpeed.</summary>
		[ElementProperty('l', readOnly: true, initialValue: 9999)]
		public double SpeedLimit { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>
		/// Set the Velocity vector, a signed speed to move the stepper in a given direction,
		/// specified as a percentage of the SpeedLimit.
		/// </summary>
		[ElementProperty('v', readOnly: false, noRequest: true, forceWrite: true)]
		public double Velocity { get => GetProperty<double>(); set => SetProperty(value, setCondition: (v) => (double)v == 0); }

		/// <summary>
		/// Get the current Speed of the stepper.
		/// </summary>
		[ElementProperty('s', readOnly: true)]
		public double Speed { get => GetProperty<double>(); set => SetProperty(value); }

		/// <summary>
		/// Move the stepper to a position, accelerating to a desired speed.
		/// </summary>
		/// <param name="position">The desired target position.</param>
		/// <param name="speed">An optional desired speed, defaulting to the stepper's MoveSpeed from Settings.</param>
		public void Move(double position, double speed, double accel)
		{
			Acceleration = accel;
			MaxSpeed = speed;
			TargetPosition = position;
		}

		/// <summary>
		/// Get/set the calibration status, true if stepper has been calibrated with a move to it's limit switch(es).
		/// </summary>
		/// <remarks>Setting to false will cause calibration to occur.</remarks>
		[ElementProperty('c', readOnly: false)]
		public bool Calibrated { get => GetProperty<bool>(); set => SetProperty(value); }

		/// <summary>
		/// Calculate the speed required to move a specified distance over a specified time
		/// with the acceleration already specified.
		/// </summary>
		/// <param name="distance">The signed distance to move, in logical units.</param>
		/// <param name="seconds">The duration, in seconds, desired for the move.</param>
		/// <returns>The target cruising speed for the move, for use with SetMaxSpeed.</returns>
		/// <remarks>
		/// Initial and final velocities are assumed equal to zero and acceleration and deceleration
		/// values are assumed equal (using the value from SetAcceleration).
		/// </remarks>
		public double MaxSpeedForDistanceAndTime(double distance, double seconds)
		{
			//
			// The area under a graph of velocity versus time is the distance traveled.
			//
			// The graph of velocity while accelerating is an upward-sloping line
			// and the area under the graph is a right triangle with height 'S' (the speed to solve for)
			// and length 'Ta' (the time spent accelerating)
			//
			// The graph of velocity while decelerating at the same rate is a mirror of the
			// acceleration case. So the total distance traveled while accelerating and decelerating
			// is the sum of the area of the two triangles:
			//      Dad = (1/2 • S•Ta) • 2
			//         = S•Ta
			//
			// The graph of the constant velocity between acceleration and deceleration is a horizontal
			// line and the area under the graph is a rectangle with height 'S' and length 'Ts':
			//      Ds = S•Ts
			//
			// The total distance traveled is:
			//      D = Dad + Ds
			//        = S•Ta + S•Ts
			//
			// Knowing the acceleration 'A' we can calculate Ta as:
			//      Ta = S / A
			//
			// And expressing total time as 'T':
			//      Ts = T - 2•Ta
			//
			// With substitution:
			//      D = S • (S / A) + S • (T - 2 • (S / A) )
			//        = S^2 / A + S•T - 2 • S^2 / A
			//        = -S^2 / A + S•T
			//
			// or, in quadratic form:
			//      S^2 / A - S•T + D = 0
			//
			// Solving for S as quadratic roots:
			//      S = (T ± SQRT(T^2 - 4•D/A) ) / (2 / A)
			//
			// The value of the discriminant [ T^2 - 4•D/A ] determines the number of solutions:
			//      < 0  -- No solution: not enough time to reach the distance
			//              with the given acceleration
			//      == 0 -- One solution: Ts == 0 and all the time is spent
			//              accelerating and decelerating
			//      > 0  -- Two solutions:
			//              The larger value represents an invalid solution where Ts < 0
			//              The smaller value represents a valid solution where Ts > 0
			//
			// NOTE: Using the discriminant we can calculate the minimum amount of time
			// required to travel the specified distance:
			//      T = SQRT(4 • D / A)
			//

			distance = Math.Abs(distance);
			// calculate the discriminant
			double disc = seconds * seconds - 4 * distance / Acceleration;
			double speed;
			if (disc == 0.0)
			{
				// one solution - all accelerating and decelerating
				speed = seconds / 2.0 * Acceleration;
			}
			else if (disc > 0.0)
			{
				// two solutions - use only the smaller, valid solution
				speed = (seconds - Math.Sqrt(disc)) / 2.0 * Acceleration;
			}
			else
			{
				// no solution - not enough time to get there
				// as a consolation, determine the minimum time required
				// and calculate the speed we hit after accelerating

				// calculate the time that makes the discriminant zero
				seconds = Math.Sqrt(4.0 * distance / Acceleration);
				// same as above, but the discriminant is zero
				// return a negative value so the caller knows we actually failed
				speed = -seconds / 2.0 * Acceleration; // one solution - all accelerating and decelerating
			}
			// note that this speed may exceed our SpeedLimit
			return speed;
		}

		/// <summary>
		/// Given the current state of the stepper, calculate the time required to move to the
		/// specified distance, arriving with the desired speed.
		/// </summary>
		/// <param name="distance">The signed distance to move.</param>
		/// <param name="speed2">The desired arrival speed, defaulting to 0.</param>
		/// <returns>The number of seconds required to move.</returns>
		public double TimeRemaining(double distance, double speed2 = 0)
		{
			if (Speed != 0 && distance != 0 && Math.Sign(distance) != Math.Sign(Speed))
			{
			//	Debug.WriteLine($"--> {Name} Not moving in the right direction, Distance: {distance} Speed: {Speed}");
				return 0;
			}
			distance = Math.Abs(distance);
			if (distance == 0)
			{
			//	Debug.WriteLine($"--> {Name} Distance is 0");
				return 0;
			}
			var speed1 = Math.Abs(Speed);
			if (speed1 == 0)
			{
			//	Debug.WriteLine($"--> {Name} Speed is 0");
				return 0;
			}
			if (Acceleration == 0)
			{
			//	Debug.WriteLine($"--> {Name} Acceleration not loaded");
				return 0;
			}

			// calculate changes in velocity for the acceleration and deceleration phases
			var deltaV1 = (MaxSpeed > speed1) ? MaxSpeed - speed1 : 0;
			var deltaV2 = (MaxSpeed > speed2) ? MaxSpeed - speed2 : 0;

			// calculate time spent in the acceleration and deceleration phases
			var ta = deltaV1 / Acceleration;
			var td = deltaV2 / Acceleration;

			// calculate distance covered by the acceleration and deceleration phases
			var da = deltaV1 * ta * 0.5;
			var dd = deltaV2 * td * 0.5;

			// calculate the distance spent traveling at the constant MaxSpeed
			var dm = distance - da - dd;
			if (dm < 0)
			{
			//	Debug.WriteLine($"--> {Name} dm < 0: {dm}");
				// there is no constant velocity phase
				// all time is spent accelerating and decelerating

				/*
				the total distance to be traveled will equal
				the sum of the distances from the acceleration and deceleration phases
				leaving an equation that can be solved for Vm = the max speed reached

				D = (1/2 • A • Ta^2) + (1/2 • A • Td^2)
					where
						D = distance to move
						A = acceleration/deceleration
						Ta = acceleration time
						Td = deceleration time
				D = (1/2 • A • (ΔVa/A)^2) + (1/2 • A • (ΔVd/A)^2)
					where
						ΔVa = change in velocity while accelerating
						ΔVd = change in velocity while decelerating
				D = (1/2 • ΔVa^2 / A) + (1/2 • ΔVd^2 / A)
				D = (1/2 • (Vm - V1)^2 / A) + (1/2 • (Vm - V2)^2 / A)
					where
						Vm is the max speed after acceleration
						V1 is the initial speed
						V2 is the final speed
				2 • D = (Vm - V1)^2 / A + (Vm - V2)^2 / A
				2 • D • A = (Vm - V1)^2 + (Vm - V2)^2
				2 • D • A = Vm^2 - 2 • Vm • V1 + V1^2 + Vm^2 - 2 • Vm • V2 + V2^2
				2 • D • A = 2 • Vm^2 - 2 • Vm • V1 - 2 • Vm • V2 + V1^2 + V2^2
				2 • D • A = 2 • Vm^2 - 2 • (V1 + V2) • Vm + V1^2 + V2^2
				0 = 2 • Vm^2 - 2 • (V1 + V2) • Vm + V1^2 + V2^2 - 2 • D • A
				*/

				// the parameters of the quadratic solution for Vm
				var a = 2;
				var b = -2 * (speed1 + speed2);
				var c = speed1 * speed1 + speed2 * speed2 - distance * 2 * Acceleration;
				// the quadratic discriminant
				var disc = b * b - 4 * a * c;

				double max;     // solving for Vm (max speed)
				if (disc == 0)
				{
					// one solution
					max = -b / (2 * a);
				//	Debug.WriteLine($"--> {Name} max: {max}");
				}
				else if (disc > 0)
				{
					// two solutions; the lesser (with the negative term) being invalid
					max = (-b + Math.Sqrt(disc)) / (2 * a);
					//	var max2 = (-b - Math.Sqrt(disc)) / (2 * a);
				//	Debug.WriteLine($"--> {Name} max1: {max}");
				}
				else
				{
					// no solution - but very close to the finish
				//	Debug.WriteLine($"--> {Name} disc: {disc}");
					return 0;
				}
				// calculate changes in velocity for the acceleration and deceleration phases
				deltaV1 = (max > speed1) ? max - speed1 : 0;
				deltaV2 = (max > speed2) ? max - speed2 : 0;
				// calculate time spent in the acceleration and deceleration phases
				ta = deltaV1 / Acceleration;
				td = deltaV2 / Acceleration;
				//	da = deltaV1 * ta * 0.5;
				//	dd = deltaV2 * td * 0.5;
				//	dm = distance - da - dd;	// should be nearly 0!
				// total the times
				var tx = ta + td;
			//	Debug.WriteLine($"--> {Name} dm: {dm} tx: {tx}");
				return tx;
			}

			// calculate the time spent at constant velocity
			var tm = dm / MaxSpeed;
			// total the times of all phases
			var t = ta + tm + td;
			if (double.IsNaN(t))
				return 0;
			return t;
		}

		/// <summary>
		/// Limit a Position value to within the valid range for the stepper.
		/// </summary>
		/// <param name="v">The Position value.</param>
		/// <returns>The Position value after limiting to the valid range.</returns>
		public double LimitValue(double v)
		{
			return Math.Max(Math.Min(v, MaxLimit), MinLimit);
		}
	}
}
