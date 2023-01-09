// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using Amethyst.Plugins.Contract;
using DeviceHandler;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DQuaternion = DeviceHandler.Quaternion;
using DVector = DeviceHandler.Vector;
using Quaternion = System.Numerics.Quaternion;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace plugin_OwoTrack;

[Export(typeof(ITrackingDevice))]
[ExportMetadata("Name", "owoTrackVR")]
[ExportMetadata("Guid", "K2VRTEAM-AME2-APII-DVCE-DVCEOWOTRACK")]
[ExportMetadata("Publisher", "K2VR Team")]
[ExportMetadata("Website", "https://github.com/KinectToVR/plugin_owoTrackVR")]
public class OwoTrack : ITrackingDevice
{
    // Update settings UI
    private int _statusBackup = (int)HandlerStatus.ServiceNotStarted;

    public OwoTrack()
    {
        // Set up a new server update timer
        var timer = new Timer
        {
            Interval = 25, AutoReset = true, Enabled = true
        };
        timer.Elapsed += (_, _) =>
        {
            if (!PluginLoaded || !Handler.IsInitialized) return;
            Handler.Update(); // Sanity check, refresh the sever
        };
        timer.Start(); // Start the timer
    }

    [Import(typeof(IAmethystHost))] private IAmethystHost Host { get; set; }

    private TrackingHandler Handler { get; } = new();
    private bool CalibrationPending { get; set; }

    private Vector3 GlobalOffset { get; } = Vector3.Zero;
    private Vector3 DeviceOffset { get; } = new(0f, -0.045f, 0.09f);

    private uint TrackerHeightOffset { get; set; } = 75;
    private Vector3 TrackerOffset => new(0f, TrackerHeightOffset / -100f, 0f);
    private Quaternion GlobalRotation { get; set; } = Quaternion.Identity;
    private Quaternion LocalRotation { get; set; } = Quaternion.Identity;

    private bool PluginLoaded { get; set; }
    private Page InterfaceRoot { get; set; }

    public bool IsSkeletonTracked => true;
    public bool IsPositionFilterBlockingEnabled => false;
    public bool IsPhysicsOverrideEnabled => false;
    public bool IsSelfUpdateEnabled => false;
    public bool IsFlipSupported => false;
    public bool IsAppOrientationSupported => false;
    public bool IsSettingsDaemonSupported => true;
    public object SettingsInterfaceRoot => InterfaceRoot;

    public ObservableCollection<TrackedJoint> TrackedJoints { get; } = new()
    {
        new TrackedJoint
        {
            Name = TrackedJointType.JointSpineWaist.ToString(),
            Role = TrackedJointType.JointSpineWaist
        }
    };

    public int DeviceStatus
    {
        get
        {
            UpdateSettingsInterface();
            return Handler.StatusResult;
        }
    }

    public Uri ErrorDocsUri => new($"https://docs.k2vr.tech/{Host?.DocsLanguageCode ?? "en"}/owo/setup/");

    public bool IsInitialized => Handler.IsInitialized;

    public string DeviceStatusString => PluginLoaded
        ? DeviceStatus switch
        {
            (int)HandlerStatus.ServiceNotStarted => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NotStarted"),
            (int)HandlerStatus.ServiceSuccess => Host.RequestLocalizedString("/Plugins/OWO/Statuses/Success"),
            (int)HandlerStatus.ConnectionDead => Host.RequestLocalizedString("/Plugins/OWO/Statuses/ConnectionDead"),
            (int)HandlerStatus.ErrorNoData => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NoData"),
            (int)HandlerStatus.ErrorInitFailed => Host.RequestLocalizedString("/Plugins/OWO/Statuses/InitFailure"),
            (int)HandlerStatus.ErrorPortsTaken => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NoPorts"),
            _ => $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what."
        }
        : $"Undefined: {DeviceStatus}\nE_UNDEFINED\nSomething weird has happened, though we can't tell what.";

    public void OnLoad()
    {
        // Try to load calibration values from settings
        TrackerHeightOffset = Host.PluginSettings.GetSetting("TrackerHeightOffset", 75U);
        GlobalRotation = Host.PluginSettings.GetSetting("GlobalRotation", Quaternion.Identity);
        LocalRotation = Host.PluginSettings.GetSetting("LocalRotation", Quaternion.Identity);

        // Try to fix the recovered height offset value
        if (TrackerHeightOffset is < 60 or > 90) TrackerHeightOffset = 75;
        TrackerHeightOffset = Math.Clamp(TrackerHeightOffset, 60, 90);

        // Try to fix recovered quaternion values
        if (GlobalRotation == Quaternion.Zero) GlobalRotation = Quaternion.Identity;
        if (LocalRotation == Quaternion.Zero) LocalRotation = Quaternion.Identity;

        // Re-register native action handlers
        Handler.StatusChanged -= StatusChangedEventHandler;
        Handler.StatusChanged += StatusChangedEventHandler;
        Handler.LogEvent -= LogMessageEventHandler;
        Handler.LogEvent += LogMessageEventHandler;

        // Copy the ret values to the handler
        Handler.GlobalRotation = GlobalRotation.ToWin();
        Handler.LocalRotation = LocalRotation.ToWin();

        // Tell the handler to initialize
        if (!PluginLoaded) Handler.OnLoad();

        // Settings UI setup
        IpTextBlock = new TextBlock
        {
            Text = Handler.IP.Length > 1 // Format as list if found multiple IPs!
                ? $"[ {string.Join(", ", Handler.IP)} ]" // Or show a placeholder
                : Handler.IP.GetValue(0)?.ToString() ?? "127.0.0.1",
            Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
        };
        IpLabelTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString(Handler.IP.Length > 1
                ? "/Plugins/OWO/Settings/Labels/LocalIP/Multiple"
                : "/Plugins/OWO/Settings/Labels/LocalIP/One"),
            Margin = new Thickness(3), Opacity = 0.5
        };

        PortTextBlock = new TextBlock
        {
            Text = Handler.Port.ToString(), // Don't allow any changes
            Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
        };
        PortLabelTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Labels/Port"),
            Margin = new Thickness(3), Opacity = 0.5
        };

        HipHeightLabelTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Labels/HipHeight"),
            Margin = new Thickness(3), Opacity = 0.5
        };

        MessageTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/NotStarted"),
            Margin = new Thickness(3), Opacity = 0.5
        };
        CalibrationTextBlock = new TextBlock { Visibility = Visibility.Collapsed };

        CalibrateForwardButton = new Button
        {
            FontWeight = FontWeights.SemiBold,
            Content = Host.RequestLocalizedString("/Plugins/OWO/Settings/Buttons/Calibration/Forward"),
            Margin = new Thickness(3), IsEnabled = false, Visibility = Visibility.Collapsed
        };
        CalibrateDownButton = new Button
        {
            FontWeight = FontWeights.SemiBold,
            Content = Host.RequestLocalizedString("/Plugins/OWO/Settings/Buttons/Calibration/Down"),
            Margin = new Thickness(3), IsEnabled = false, Visibility = Visibility.Collapsed
        };

        HipHeightNumberBox = new NumberBox
        {
            Value = TrackerHeightOffset, Margin = new Thickness { Left = 5 },
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };

        InterfaceRoot = new Page
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { IpLabelTextBlock, IpTextBlock }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { PortLabelTextBlock, PortTextBlock },
                        Margin = new Thickness { Bottom = 10 }
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { HipHeightLabelTextBlock, HipHeightNumberBox },
                        Margin = new Thickness { Bottom = 15 }
                    },
                    MessageTextBlock,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { CalibrateForwardButton, CalibrateDownButton },
                        Margin = new Thickness { Bottom = 15 }
                    },
                    CalibrationTextBlock
                }
            }
        };

        // Setup signals
        HipHeightNumberBox.ValueChanged += (sender, _) =>
        {
            // Try to fix the recovered height offset value
            if (double.IsNaN(sender.Value)) sender.Value = 75;
            if (sender.Value is < 60 or > 90)
                sender.Value = Math.Clamp(sender.Value, 60, 90);

            TrackerHeightOffset = (uint)sender.Value; // Also save!
            Host.PluginSettings.SetSetting("TrackerHeightOffset", TrackerHeightOffset);
        };

        CalibrateForwardButton.Click += CalibrateForwardButton_Click;
        CalibrateDownButton.Click += CalibrateDownButton_Click;

        // Mark the plugin as loaded
        PluginLoaded = true;
        UpdateSettingsInterface(true);
    }

    public void Initialize()
    {
        switch (Handler.Initialize())
        {
            case (int)HandlerStatus.ServiceNotStarted:
                Host.Log(
                    $"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ServiceNotStarted}",
                    LogSeverity.Warning);
                break;
            case (int)HandlerStatus.ServiceSuccess:
                Host.Log(
                    $"Successfully initialized the owoTrackVR device handler! Status: {HandlerStatus.ServiceSuccess}");
                break;
            case (int)HandlerStatus.ConnectionDead:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ConnectionDead}",
                    LogSeverity.Warning);
                break;
            case (int)HandlerStatus.ErrorNoData:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ErrorNoData}",
                    LogSeverity.Warning);
                break;
            case (int)HandlerStatus.ErrorInitFailed:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ErrorInitFailed}",
                    LogSeverity.Error);
                break;
            case (int)HandlerStatus.ErrorPortsTaken:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {HandlerStatus.ErrorPortsTaken}",
                    LogSeverity.Fatal);
                break;
        }

        // Refresh the settings interface
        UpdateSettingsInterface(true);
    }

    public void Shutdown()
    {
        switch (Handler.Shutdown())
        {
            case 0:
                Host.Log($"Tried to shutdown the owoTrackVR device handler with status: {Handler.StatusResult}");
                break;
            default:
                Host.Log("Tried to shutdown the owoTrackVR device handler, exception occurred!", LogSeverity.Error);
                break;
        }
    }

    public void Update()
    {
        // That's all if the server is failing!
        if (!PluginLoaded || !Handler.IsInitialized ||
            Handler.StatusResult != (int)HandlerStatus.ServiceSuccess) return;

        // Get the computed pose
        var pose = Handler.CalculatePose(
            new Pose
            {
                Position = Host.HmdPose.Position.ToWin(),
                Orientation = Host.HmdPose.Orientation.ToWin()
            },
            (float)Host.HmdOrientationYaw,
            GlobalOffset.ToWin(),
            DeviceOffset.ToWin(),
            TrackerOffset.ToWin()
        );

        // Update our tracker's pose
        TrackedJoints[0].Position = pose.Position.ToNet();
        TrackedJoints[0].Orientation = pose.Orientation.ToNet();
    }

    public void SignalJoint(int jointId)
    {
        // Send a buzz signal
        Handler.Signal();
    }

    // "Full Calibration"
    private async void CalibrateForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Handler.IsInitialized || CalibrationPending) return; // Sanity check

        // Block next clicks
        CalibrateForwardButton.IsEnabled = false;
        CalibrateDownButton.IsEnabled = false;
        CalibrationPending = true;

        // Setup calibration UI
        CalibrationTextBlock.Visibility = Visibility.Visible;
        CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Instructions/Forward");

        // Wait as bit
        await Task.Delay(7000);
        if (!Handler.IsInitialized)
        {
            Handler.CalibratingForward = false;
            CalibrationTextBlock.Visibility = Visibility.Collapsed;
            return; // Sanity check, abort
        }

        // Begin calibration
        Handler.CalibratingForward = true;
        CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/Still");
        await Task.Delay(4000); // Wait as bit

        // End calibration
        Handler.CalibratingForward = false;
        CalibrationTextBlock.Visibility = Visibility.Collapsed;

        CalibrateForwardButton.IsEnabled = true;
        CalibrateDownButton.IsEnabled = true;
        CalibrationPending = false;

        // Copy and save settings
        GlobalRotation = Handler.GlobalRotation.ToNet();
        LocalRotation = Handler.LocalRotation.ToNet();

        Host.PluginSettings.SetSetting("GlobalRotation", GlobalRotation);
        Host.PluginSettings.SetSetting("LocalRotation", LocalRotation);

        // Update the UI
        UpdateSettingsInterface();
    }

    // "Down Calibration"
    private async void CalibrateDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Handler.IsInitialized || CalibrationPending) return; // Sanity check

        // Block next clicks
        CalibrateForwardButton.IsEnabled = false;
        CalibrateDownButton.IsEnabled = false;
        CalibrationPending = true;

        // Setup calibration UI
        CalibrationTextBlock.Visibility = Visibility.Visible;
        CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Instructions/Down");

        // Wait as bit
        await Task.Delay(7000);
        if (!Handler.IsInitialized)
        {
            Handler.CalibratingDown = false;
            CalibrationTextBlock.Visibility = Visibility.Collapsed;
            return; // Sanity check, abort
        }

        // Begin calibration
        Handler.CalibratingDown = true;
        CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/Still");
        await Task.Delay(4000); // Wait as bit

        // End calibration
        Handler.CalibratingDown = false;
        CalibrationTextBlock.Visibility = Visibility.Collapsed;

        CalibrateForwardButton.IsEnabled = true;
        CalibrateDownButton.IsEnabled = true;
        CalibrationPending = false;

        // Copy and save settings
        GlobalRotation = Handler.GlobalRotation.ToNet();
        LocalRotation = Handler.LocalRotation.ToNet();

        Host.PluginSettings.SetSetting("GlobalRotation", GlobalRotation);
        Host.PluginSettings.SetSetting("LocalRotation", LocalRotation);

        // Update the UI
        UpdateSettingsInterface();
    }

    private void UpdateSettingsInterface(bool force = false)
    {
        if (!PluginLoaded || !Handler.IsInitialized ||
            Handler.CalibratingForward || Handler.CalibratingDown) return;

        // Nothing's changed, no need to update
        if (!force && Handler.StatusResult == _statusBackup) return;

        // Update the settings UI
        if (Handler.StatusResult == (int)HandlerStatus.ServiceSuccess)
        {
            MessageTextBlock.Visibility = Visibility.Collapsed;

            CalibrateForwardButton.Visibility = Visibility.Visible;
            CalibrateDownButton.Visibility = Visibility.Visible;

            if (!CalibrationPending)
            {
                CalibrateForwardButton.IsEnabled = true;
                CalibrateDownButton.IsEnabled = true;
            }
        }
        else
        {
            MessageTextBlock.Visibility = Visibility.Visible;
            MessageTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/NotConnected");

            CalibrateForwardButton.Visibility = Visibility.Collapsed;
            CalibrateDownButton.Visibility = Visibility.Collapsed;

            if (!CalibrationPending)
            {
                CalibrateForwardButton.IsEnabled = false;
                CalibrateDownButton.IsEnabled = false;
            }
        }

        // Cache the status
        _statusBackup = Handler.StatusResult;
    }

    private void StatusChangedEventHandler(object sender, string message)
    {
        // Log what happened
        Host?.Log($"Status interface requested by {sender} with message {message}");

        // Request an interface refresh
        Host?.RefreshStatusInterface();
    }

    private void LogMessageEventHandler(object sender, string message)
    {
        // Compute severity
        var severity = message.Length >= 2
            ? int.TryParse(message[1].ToString(), out var parsed) ? Math.Clamp(parsed, 0, 3) : 0
            : 0; // Default to LogSeverity.Info

        // Log a message to AME
        Host?.Log(message, (LogSeverity)severity);
    }

    private enum HandlerStatus
    {
        ServiceNotStarted = 0x00010005, // Not initialized
        ServiceSuccess = 0, // Success, everything's fine!
        ConnectionDead = 0x00010001, // No connection
        ErrorNoData = 0x00010002, // No data received
        ErrorInitFailed = 0x00010003, // Init failed
        ErrorPortsTaken = 0x00010004 // Ports taken
    }

    #region UI Elements

    private TextBlock IpTextBlock { get; set; }
    private TextBlock IpLabelTextBlock { get; set; }
    private TextBlock PortTextBlock { get; set; }
    private TextBlock PortLabelTextBlock { get; set; }
    private TextBlock HipHeightLabelTextBlock { get; set; }
    private TextBlock MessageTextBlock { get; set; }
    private TextBlock CalibrationTextBlock { get; set; }

    private Button CalibrateForwardButton { get; set; }
    private Button CalibrateDownButton { get; set; }

    private NumberBox HipHeightNumberBox { get; set; }

    #endregion
}

internal static class ProjectionExtensions
{
    public static DVector ToWin(this Vector3 vector)
    {
        return new DVector(vector.X, vector.Y, vector.Z);
    }

    public static DQuaternion ToWin(this Quaternion quaternion)
    {
        return new DQuaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }

    public static Vector3 ToNet(this DVector vector)
    {
        return new Vector3(vector.X, vector.Y, vector.Z);
    }

    public static Quaternion ToNet(this DQuaternion quaternion)
    {
        return new Quaternion(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);
    }
}