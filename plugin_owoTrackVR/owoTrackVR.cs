using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using Amethyst.Plugins.Contract;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace plugin_OwoTrack;

[Export(typeof(ITrackingDevice))]
[ExportMetadata("Name", "owoTrackVR")]
[ExportMetadata("Guid", "K2VRTEAM-AME2-APII-DVCE-DVCEOWOTRACK")]
[ExportMetadata("Publisher", "K2VR Team")]
[ExportMetadata("Version", "1.0.0.1")]
[ExportMetadata("Website", "https://github.com/KinectToVR/plugin_owoTrackVR")]
[ExportMetadata("DependencyLink", "https://docs.k2vr.tech/{0}/owo/about/")]
[ExportMetadata("CoreSetupData", typeof(SetupData))]
public class OwoTrack : ITrackingDevice
{
    // Update settings UI
    private Handler.ConnectionStatus _statusBackup = Handler.ConnectionStatus.NotStarted;

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

    private Handler Handler { get; } = new(null);
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

    public ObservableCollection<TrackedJoint> TrackedJoints { get; } =
    [
        new()
        {
            Name = nameof(TrackedJointType.JointSpineWaist),
            Role = TrackedJointType.JointSpineWaist
        }
    ];

    public int DeviceStatus
    {
        get
        {
            UpdateSettingsInterface();
            return (int)Handler.Status;
        }
    }

    public Uri ErrorDocsUri => new($"https://docs.k2vr.tech/{Host?.DocsLanguageCode ?? "en"}/owo/setup/");

    public bool IsInitialized => Handler.IsInitialized;

    public string DeviceStatusString => PluginLoaded
        ? DeviceStatus switch
        {
            (int)Handler.ConnectionStatus.NotStarted => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NotStarted"),
            (int)Handler.ConnectionStatus.Ok => Host.RequestLocalizedString("/Plugins/OWO/Statuses/Success"),
            (int)Handler.ConnectionStatus.ConnectionDead => Host.RequestLocalizedString("/Plugins/OWO/Statuses/ConnectionDead"),
            (int)Handler.ConnectionStatus.NoData => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NoData"),
            (int)Handler.ConnectionStatus.InitFailed => Host.RequestLocalizedString("/Plugins/OWO/Statuses/InitFailure"),
            (int)Handler.ConnectionStatus.PortsTaken => Host.RequestLocalizedString("/Plugins/OWO/Statuses/NoPorts"),
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

        // Copy the ret values to the handler
        Handler.GlobalRotation = GlobalRotation;
        Handler.LocalRotation = LocalRotation;

        // Tell the handler to initialize
        if (!PluginLoaded) Handler.OnLoad();

        // Settings UI setup
        IpTextBlock = new TextBlock
        {
            Text = Handler.Addresses.Count > 1 // Format as list if found multiple IPs!
                ? $"[ {string.Join(", ", Handler.Addresses)} ]" // Or show a placeholder
                : Handler.Addresses.ElementAtOrDefault(0) ?? "127.0.0.1",
            Margin = new Thickness { Left = 5, Top = 3, Right = 3, Bottom = 3 }
        };
        IpLabelTextBlock = new TextBlock
        {
            Text = Host.RequestLocalizedString(Handler.Addresses.Count > 1
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
            Host.PlayAppSound(SoundType.Invoke);
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
            case Handler.ConnectionStatus.NotStarted:
                Host.Log(
                    $"Couldn't initialize the owoTrackVR device handler! Status: {Handler.Status}",
                    LogSeverity.Warning);
                break;
            case Handler.ConnectionStatus.Ok:
                Host.Log(
                    $"Successfully initialized the owoTrackVR device handler! Status: {Handler.Status}");
                break;
            case Handler.ConnectionStatus.ConnectionDead:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {Handler.Status}",
                    LogSeverity.Warning);
                break;
            case Handler.ConnectionStatus.NoData:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {Handler.Status}",
                    LogSeverity.Warning);
                break;
            case Handler.ConnectionStatus.InitFailed:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {Handler.Status}",
                    LogSeverity.Error);
                break;
            case Handler.ConnectionStatus.PortsTaken:
                Host.Log($"Couldn't initialize the owoTrackVR device handler! Status: {Handler.Status}",
                    LogSeverity.Fatal);
                break;
        }

        // Refresh the settings interface
        UpdateSettingsInterface(true);
    }

    public void Shutdown()
    {
        Handler.Shutdown();
        Host.Log($"Tried to shutdown the owoTrackVR device handler with status: {Handler.Status}");
    }

    public void Update()
    {
        // That's all if the server is failing!
        if (!PluginLoaded || !Handler.IsInitialized ||
            Handler.Status is not Handler.ConnectionStatus.Ok) return;

        // Get the computed pose
        var pose = Handler.CalculatePose(
            (
                Host.HmdPose.Position,
                Host.HmdPose.Orientation,
                (float)Host.HmdOrientationYaw
            ),
            GlobalOffset,
            DeviceOffset,
            TrackerOffset
        );

        // Update our tracker's pose
        TrackedJoints[0].Position = pose.Position;
        TrackedJoints[0].Orientation = pose.Orientation;
    }

    public void SignalJoint(int jointId)
    {
        // Send a buzz signal
        Handler.Signal();
    }

    // "Full Calibration"
    private async void CalibrateForwardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Handler.IsInitialized || CalibrationPending) return; // Sanity check

            // Block next clicks
            CalibrateForwardButton.IsEnabled = false;
            CalibrateDownButton.IsEnabled = false;
            CalibrationPending = true;

            // Setup calibration UI
            CalibrationTextBlock.Visibility = Visibility.Visible;
            CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Instructions/Forward");
            Host.PlayAppSound(SoundType.CalibrationStart);

            // Wait as bit
            await Task.Delay(7000);
            if (!Handler.IsInitialized)
            {
                Handler.CalibratingForward = false;
                CalibrationTextBlock.Visibility = Visibility.Collapsed;
                Host.PlayAppSound(SoundType.CalibrationAborted);
                return; // Sanity check, abort
            }

            // Begin calibration
            Handler.CalibratingForward = true;
            CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/Still");
            Host.PlayAppSound(SoundType.CalibrationPointCaptured);

            await Task.Delay(4000); // Wait as bit
            Host.PlayAppSound(SoundType.CalibrationComplete);

            // End calibration
            Handler.CalibratingForward = false;
            CalibrationTextBlock.Visibility = Visibility.Collapsed;

            CalibrateForwardButton.IsEnabled = true;
            CalibrateDownButton.IsEnabled = true;
            CalibrationPending = false;

            // Copy and save settings
            GlobalRotation = Handler.GlobalRotation;
            LocalRotation = Handler.LocalRotation;

            Host.PluginSettings.SetSetting("GlobalRotation", GlobalRotation);
            Host.PluginSettings.SetSetting("LocalRotation", LocalRotation);

            // Update the UI
            UpdateSettingsInterface();
        }
        catch (Exception ex)
        {
            Host?.Log(ex);
        }
    }

    // "Down Calibration"
    private async void CalibrateDownButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Handler.IsInitialized || CalibrationPending) return; // Sanity check

            // Block next clicks
            CalibrateForwardButton.IsEnabled = false;
            CalibrateDownButton.IsEnabled = false;
            CalibrationPending = true;

            // Setup calibration UI
            CalibrationTextBlock.Visibility = Visibility.Visible;
            CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Instructions/Down");
            Host.PlayAppSound(SoundType.CalibrationStart);

            // Wait as bit
            await Task.Delay(7000);
            if (!Handler.IsInitialized)
            {
                Handler.CalibratingDown = false;
                CalibrationTextBlock.Visibility = Visibility.Collapsed;
                Host.PlayAppSound(SoundType.CalibrationAborted);
                return; // Sanity check, abort
            }

            // Begin calibration
            Handler.CalibratingDown = true;
            CalibrationTextBlock.Text = Host.RequestLocalizedString("/Plugins/OWO/Settings/Notices/Still");
            Host.PlayAppSound(SoundType.CalibrationPointCaptured);

            await Task.Delay(4000); // Wait as bit
            Host.PlayAppSound(SoundType.CalibrationComplete);

            // End calibration
            Handler.CalibratingDown = false;
            CalibrationTextBlock.Visibility = Visibility.Collapsed;

            CalibrateForwardButton.IsEnabled = true;
            CalibrateDownButton.IsEnabled = true;
            CalibrationPending = false;

            // Copy and save settings
            GlobalRotation = Handler.GlobalRotation;
            LocalRotation = Handler.LocalRotation;

            Host.PluginSettings.SetSetting("GlobalRotation", GlobalRotation);
            Host.PluginSettings.SetSetting("LocalRotation", LocalRotation);

            // Update the UI
            UpdateSettingsInterface();
        }
        catch (Exception ex)
        {
            Host?.Log(ex);
        }
    }

    private void UpdateSettingsInterface(bool force = false)
    {
        if (!PluginLoaded || !Handler.IsInitialized ||
            Handler.CalibratingForward || Handler.CalibratingDown) return;

        // Nothing's changed, no need to update
        if (!force && Handler.Status == _statusBackup) return;

        // Update the settings UI
        if (Handler.Status == (int)Handler.ConnectionStatus.Ok)
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
        _statusBackup = Handler.Status;
    }

    private void StatusChangedEventHandler(object sender, string message)
    {
        // Log what happened
        Host?.Log($"Status interface requested by {sender} with message {message}");

        // Request an interface refresh
        Host?.RefreshStatusInterface();
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

internal class SetupData : ICoreSetupData
{
    public object PluginIcon => new PathIcon
    {
        Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry),
            "M34.86,0A34.76,34.76,0,1,0,69.51,34.9,34.74,34.74,0,0,0,34.86,0ZM14.33,18.66A25.12,25.12,0,0,1,29.22,9.31c1.81-.39,3.68-.53,5-.7a28.81,28.81,0,0,1,11,2.27,1.45,1.45,0,0,1,1,1.45q.3,4.06.75,8.11c.09.79-.21.82-.79.69-.36-.08-.72-.16-1.08-.26-2-.59-1.88-.22-1.82-2.46,0-1.38-.42-1.79-1.73-1.64s-2.87.17-4.3.34c-.28,0-.59.43-.76.72a1.43,1.43,0,0,0,0,.8c.12,1.07-.07,1.62-1.41,1.84a31.91,31.91,0,0,0-16.08,7.77c-.56.48-.86.48-1.18-.2-1.22-2.62-2.5-5.21-3.67-7.85A1.86,1.86,0,0,1,14.33,18.66ZM24.91,58.73l-.09-.14h-.05v0c-2.26-.16-3.92-1.62-5.56-2.89A25.59,25.59,0,0,1,8.92,38.08a27.88,27.88,0,0,1,.87-10.71c.39.68.71,1.18,1,1.71q7,14.51,14,29a1.27,1.27,0,0,1,.05.44h0l0,0,.09,0S24.91,58.7,24.91,58.73Zm21.17-.43a29.5,29.5,0,0,1-8.72,2.43c-.18,0-.66-.57-.65-.86,0-1.86.16-3.72.28-5.58s.28-3.82.43-5.73c.1-1.25.22-2.5.34-4a4.44,4.44,0,0,1,.75.64c2.65,3.87,5.27,7.76,8,11.6C47,57.61,47,57.89,46.08,58.3Zm2.79-9.88C45.58,43.62,42.25,38.85,39,34c-.8-1.2-1.55-1.74-3-1.06a13.43,13.43,0,0,1-2.29.67c-2.05.56-2.14.68-2.27,2.76Q31,44.77,30.45,53.13a5.59,5.59,0,0,1-.29.83l-4.82-10c-1.32-2.75-2.58-5.52-4-8.23a1.65,1.65,0,0,1,.45-2.32,27.53,27.53,0,0,1,14-7.25c.83-.16,1.23.05,1.14.95,0,.32,0,.64,0,1,0,1.08.12,1.69,1.54,1.34a18.06,18.06,0,0,1,4.29-.32c.94,0,1.06-.46,1-1.2-.18-1.24.42-1.31,1.42-1,2.29.66,2.45.86,2.67,4.06.25,3.62.48,7.23.77,10.84.18,2.2.46,4.4.69,6.59Zm7.59.79-.4-.09Q54.69,33,53.31,16.94c2.24,1,5.91,7.4,6.84,11.78A26.07,26.07,0,0,1,56.46,49.21Z")
    };

    public string GroupName => string.Empty;
    public Type PluginType => typeof(ITrackingDevice);
}