#include "pch.h"
#include "TrackingHandler.h"
#include "TrackingHandler.g.cpp"

namespace winrt::DeviceHandler::implementation
{
	// String to Wide String (The better one)
	std::wstring StringToWString(const std::string& str)
	{
		const int count = MultiByteToWideChar(CP_UTF8, 0, str.c_str(), str.length(), nullptr, 0);
		std::wstring w_str(count, 0);
		MultiByteToWideChar(CP_UTF8, 0, str.c_str(), str.length(), w_str.data(), count);
		return w_str;
	}

	void TrackingHandler::OnLoad()
	{
		// Get the current internet connection profile
		const auto& profile = Windows::Networking::Connectivity::NetworkInformation::GetInternetConnectionProfile();

		// Refresh all local host IP addresses
		for (const auto& hostName : Windows::Networking::Connectivity::NetworkInformation::GetHostNames())
			if (hostName.IPInformation() && hostName.IPInformation().NetworkAdapter() &&
				hostName.IPInformation().NetworkAdapter().NetworkAdapterId() == profile.NetworkAdapter().NetworkAdapterId() &&
				hostName.Type() == Windows::Networking::HostNameType::Ipv4) ipVector.push_back(hostName.CanonicalName());
	}

	void TrackingHandler::Update()
	{
		if (initialized)
		{
			/* Update the discovery server here */
			try
			{
				infoServer->tick();
			}
			catch (std::system_error& e)
			{
				Log(L"OWO Device Error: Info server tick (heartbeat) failed!", 2);
				Log(std::format(L"Error message: {}", to_hstring(e.what())), 2);
			}

			/* Update the data server here */
			try
			{
				dataServer->tick();
			}
			catch (std::system_error& e)
			{
				Log(L"OWO Device Error: Data listener tick (heartbeat) failed!", 2);
				Log(std::format(L"Error message: {}", to_hstring(e.what())), 2);
			}

			if (!dataServer->isDataAvailable())
			{
				if (eRetries >= 180)
				{
					eRetries = 0; // Reset
					statusResult =
						dataServer->isConnectionAlive()
							? R_E_NO_DATA
							: R_E_CON_DEAD;

					// Notify about the change
					statusChangedEvent(*this, L"STATUS ERROR");
				}
				else eRetries++;
			}
			else
			{
				const auto previousStatus = statusResult;
				statusResult = S_OK; // All fine now!

				// If wasn't ok for some reason
				if (previousStatus != S_OK)
					statusChangedEvent(*this, L"STATUS OK");
			}
		}
	}

	void TrackingHandler::Signal() const
	{
		dataServer->buzz(0.7, 100.0, 0.5);
	}

	int32_t TrackingHandler::Initialize()
	{
		// Optionally initialize the server
		// (Warning: this can be done only once)
		if (statusResult == R_E_NOT_STARTED)
		{
			// Construct the networking server
			dataServer = new UDPDeviceQuatServer(&devicePort, Log);

			bool _return = false;
			infoServer = new InfoServer(_return, Log);

			if (!_return)
			{
				Log(L"OWO Device Error: Failed to bind ports!", 2);
				statusResult = R_E_PORTS_TAKEN;
				return R_E_PORTS_TAKEN; // Give up
			}

			infoServer->set_port_no(dataServer->get_port());
			infoServer->add_tracker();

			// Start listening
			try
			{
				dataServer->startListening(_return);

				if (!_return)
				{
					Log(L"OWO Device Error: Failed to bind ports!", 2);
					statusResult = R_E_PORTS_TAKEN;
					return R_E_PORTS_TAKEN; // Give up
				}

				statusResult = R_E_CON_DEAD;
			}
			catch (std::system_error& e)
			{
				Log(L"OWO Device Error: Failed to start the data listener up!", 2);
				Log(std::format(L"Error message: {}", to_hstring(e.what())), 2);
				statusResult = R_E_INIT_FAILED;
				return R_E_INIT_FAILED; // Give up
			}
		}

		// Mark the device as initialized
		if (statusResult != R_E_INIT_FAILED &&
			statusResult != R_E_PORTS_TAKEN)
		{
			initialized = true;
			calibratingForward = false;
			calibratingDown = false;
			return S_OK; // All fine now!
		}

		return statusResult; // Unknown
	}

	int32_t TrackingHandler::Shutdown()
	{
		// Turn your device off here
		initialized = false;
		return 0; // Should be ok!
	}

	int32_t TrackingHandler::Port() const
	{
		return devicePort; // NOLINT(bugprone-narrowing-conversions, cppcoreguidelines-narrowing-conversions)
	}

	void TrackingHandler::Port(int32_t value)
	{
		devicePort = value;
	}

	com_array<hstring> TrackingHandler::IP() const
	{
		return com_array(ipVector);
	}

	bool TrackingHandler::IsInitialized() const
	{
		return initialized;
	}

	int32_t TrackingHandler::StatusResult() const
	{
		return statusResult;
	}

	bool TrackingHandler::CalibratingForward() const
	{
		return calibratingForward;
	}

	void TrackingHandler::CalibratingForward(bool value)
	{
		calibratingForward = value;
	}

	bool TrackingHandler::CalibratingDown() const
	{
		return calibratingDown;
	}

	void TrackingHandler::CalibratingDown(bool value)
	{
		calibratingDown = value;
	}

	Quaternion TrackingHandler::GlobalRotation() const
	{
		return globalRotation;
	}

	void TrackingHandler::GlobalRotation(const Quaternion& value)
	{
		globalRotation = value;
	}

	Quaternion TrackingHandler::LocalRotation() const
	{
		return localRotation;
	}

	void TrackingHandler::LocalRotation(const Quaternion& value)
	{
		localRotation = value;
	}

	event_token TrackingHandler::StatusChanged(
		const Windows::Foundation::EventHandler<hstring>& handler)
	{
		return statusChangedEvent.add(handler);
	}

	void TrackingHandler::StatusChanged(const event_token& token) noexcept
	{
		statusChangedEvent.remove(token);
	}

	event_token TrackingHandler::LogEvent(
		const Windows::Foundation::EventHandler<hstring>& handler)
	{
		return logEvent.add(handler);
	}

	void TrackingHandler::LogEvent(const event_token& token) noexcept
	{
		logEvent.remove(token);
	}

	Pose TrackingHandler::CalculatePose(
		const Pose& headsetPose,
		const float& headsetYaw,
		const Vector& globalOffset,
		const Vector& deviceOffset,
		const Vector& trackerOffset)
	{
		// Make sure that we're running correctly
		if (!initialized || statusResult != S_OK)
			return {
				Vector{0, 0, 0},
				Quaternion{0, 0, 0, 1}
			};

		/* Prepare for the position calculations */

		Vector3 offset_global(globalOffset.X, globalOffset.Y, globalOffset.Z);
		Vector3 offset_local_device(deviceOffset.X, deviceOffset.Y, deviceOffset.Z);
		Vector3 offset_local_tracker(trackerOffset.X, trackerOffset.Y, trackerOffset.Z);

		Pose pose{
			.Position = Vector(
				headsetPose.Position.X,
				headsetPose.Position.Y,
				headsetPose.Position.Z
			)
		}; // Zero the position vector

		const Basis offset_basis({
			headsetPose.Orientation.X,
			headsetPose.Orientation.Y,
			headsetPose.Orientation.Z,
			headsetPose.Orientation.W
		});

		/* Parse and calculate the positions */

		// Acceleration is not used as of now
		// double* acceleration = m_data_server->getAccel();

		const double* p_remote_rotation = dataServer->getRotationQuaternion();

		auto p_remote_quaternion = Quat(
			p_remote_rotation[0], p_remote_rotation[1],
			p_remote_rotation[2], p_remote_rotation[3]);

		p_remote_quaternion =
			Quat(Vector3(1, 0, 0), -Math_PI / 2.0) * p_remote_quaternion;

		if (calibratingForward)
		{
			globalRotation = Quat(Vector3(
				0, get_yaw(p_remote_quaternion) -
				get_yaw(offset_basis, Vector3(0, 0, -1)), 0)).toWinRT();

			offset_global = (offset_basis.xform(Vector3(0, 0, -1)) *
				Vector3(1, 0, 1)).normalized() + Vector3(0, 0.2, 0);
			offset_local_device = Vector3(0, 0, 0);
			offset_local_tracker = Vector3(0, 0, 0);
		}

		p_remote_quaternion = Quat(globalRotation) * p_remote_quaternion;

		if (calibratingDown)
			localRotation =
			(Quat(p_remote_quaternion.inverse().get_euler_yxz()) *
				Quat(Vector3(0, 1, 0), -headsetYaw)).toWinRT();

		p_remote_quaternion = p_remote_quaternion * Quat(localRotation);
		pose.Orientation = p_remote_quaternion.toWinRT();
		
		// Angular velocity is not used as of now
		// double* gyro = m_data_server->getGyroscope();

		const auto final_tracker_basis = Basis(p_remote_quaternion);
		pose.Position.X += static_cast<float>(offset_global.get_axis(0) +
			offset_basis.xform(offset_local_device).get_axis(0) +
			final_tracker_basis.xform(offset_local_tracker).get_axis(0));
		pose.Position.Y += static_cast<float>(offset_global.get_axis(1) +
			offset_basis.xform(offset_local_device).get_axis(1) +
			final_tracker_basis.xform(offset_local_tracker).get_axis(1));
		pose.Position.Z += static_cast<float>(offset_global.get_axis(2) +
			offset_basis.xform(offset_local_device).get_axis(2) +
			final_tracker_basis.xform(offset_local_tracker).get_axis(2));

		// Return our results
		return pose;
	}
}
