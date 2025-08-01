<h1 dir=auto>
<b>owoTrackVR</b>
<a style="color:#9966cc;" href="https://github.com/KinectToVR/Amethyst">Amethyst</a>
<text>device plugin</text>
</h1>

## **License**
This project is licensed under the GNU GPL v3 License 

## **Overview**
This repo is a projected implementation of the `ITrackingDevice` interface,  
providing Amethyst support for owoTrackVR, using the [official handler code](https://github.com/abb128/owo-track-overlay).  
[The device handler](https://github.com/KinectToVR/plugin_owoTrackVR/tree/master/DeviceHandler) is written in C++/WinRT, and [the plugin itself](https://github.com/KinectToVR/plugin_owoTrackVR/tree/master/plugin_owoTrackVR) is written in C#

## **Downloads**
You're going to find built plugins in [repo Releases](https://github.com/KinectToVR/plugin_owoTrackVR/releases/latest).

## **Build & Deploy**
Both build and deployment instructions [are available here](https://github.com/KinectToVR/plugin_owoTrackVR/blob/master/.github/workflows/build.yml).
 - Open in Visual Studio and publish using the prepared publish profile  
   (`plugin_owoTrackVR` → `Publish` → `Publish` → `Open folder`)
 - Copy the published plugin to the `plugins` folder of your local Amethyst installation  
   or register by adding it to `$env:AppData\Amethyst\amethystpaths.k2path`
   ```jsonc
   {
    "external_plugins": [
        // Add the published plugin path here, this is an example:
        "F:\\source\\repos\\plugin_owoTrackVR\\plugin_owoTrackVR\\bin\\Release\\Publish"
    ]
   }
   ```

## **Wanna make one too? (K2API Devices Docs)**
[This repository](https://github.com/KinectToVR/Amethyst.Plugins.Templates) contains templates for plugin types supported by Amethyst.<br>
Install the templates by `dotnet new install Amethyst.Plugins.Templates::1.2.0`  
and use them in Visual Studio (recommended) or straight from the DotNet CLI.  
The project templates already contain most of the needed documentation,  
although please feel free to check out [the official website](https://docs.k2vr.tech/) for more docs sometime.

The build and publishment workflow is the same as in this repo (excluding vendor deps).  
