# Libs
**Libs** is a library of reusable components for cross-platform (Android and UWP) development.

The software is provided as a [Visual Studio](https://visualstudio.microsoft.com/free-developer-offers/) projects, with a free Community version of the tools available.

The libraries included:
* **Elements** - A framework for organizing and communicating with hardware components implemented on Arduino using the Applet framework found in the mLibs project.
	* **RemoteElement** - Parent class for Elements and the basis of the Elements framework.
	* **StepperElement** - Designed to work with the FMStepper Applet in the Arduino mLibs library.
	* **IntervalometerElement** - Designed to work with the FMIvalometer Applet in the Arduino mLibs library.
* **Platforms/Portable** - Reusable components for functionality that is platform-independent.
* **Platforms/Android** - Reusable components for functionality that is dependent on the Android platform.
* **Platforms/UWP** - Reusable components for functionality that is dependent on the Universal Windows Platform (UWP).

The Elements are designed to communicate with corresponding C++ "Applets" found in the mLibs project in a framework supported by other projects in the mLibs folder.
See the mSlider project for an example of using the Applet framework for building Arduino implementations that communicate with a cross-platform control app using the Elements framework.

Place the Libs projects in sibling subdirectories with your main projects in your file system to insure proper compilation.
