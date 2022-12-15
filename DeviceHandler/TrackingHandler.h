#pragma once
#include "TrackingHandler.g.h"

#include <chrono>
#include <fstream>
#include <thread>
#include <WinSock2.h>
#include <iphlpapi.h>
#include <WS2tcpip.h>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "iphlpapi.lib")

#include <InfoServer.h>
#include <PositionPredictor.h>
#include <UDPDeviceQuatServer.h>

/* Status enumeration */
#define R_E_CON_DEAD 0x00010001    // No connection
#define R_E_NO_DATA 0x00010002     // No data received
#define R_E_INIT_FAILED 0x00010003 // Init failed
#define R_E_PORTS_TAKEN 0x00010004 // Ports taken
#define R_E_NOT_STARTED 0x00010005 // Disconnected (initial)

namespace winrt::DeviceHandler::implementation
{
	struct TrackingHandler : TrackingHandlerT<TrackingHandler>
	{
		TrackingHandler() = default;

		void OnLoad();
		void Update();
		void Signal() const;

		int32_t Initialize();
		int32_t Shutdown();

		int32_t Port() const;
		void Port(int32_t value);

		com_array<hstring> IP() const;
		bool IsInitialized() const;
		int32_t StatusResult() const;

		bool CalibratingForward() const;
		void CalibratingForward(bool value);

		bool CalibratingDown() const;
		void CalibratingDown(bool value);

		Quaternion GlobalRotation() const;
		void GlobalRotation(const Quaternion& value);

		Quaternion LocalRotation() const;
		void LocalRotation(const Quaternion& value);

		event_token StatusChanged(const Windows::Foundation::EventHandler<hstring>& handler);
		void StatusChanged(const event_token& token) noexcept;

		event_token LogEvent(const Windows::Foundation::EventHandler<hstring>& handler);
		void LogEvent(const event_token& token) noexcept;

		Pose CalculatePose(
			const Pose& headsetPose,
			const float& headsetYaw,
			const Vector& globalOffset,
			const Vector& deviceOffset,
			const Vector& trackerOffset);

	private:
		event<Windows::Foundation::EventHandler<hstring>> statusChangedEvent;
		event<Windows::Foundation::EventHandler<hstring>> logEvent;

		std::function<void(std::wstring, int32_t)> Log = std::bind(
			&TrackingHandler::LogMessage, this, std::placeholders::_1, std::placeholders::_2);

		bool initialized = false;
		bool calibratingForward = false;
		bool calibratingDown = false;

		uint32_t devicePort = 6969;

		std::vector<hstring> ipVector;
		HRESULT statusResult = R_E_NOT_STARTED;

		UDPDeviceQuatServer* dataServer;
		InfoServer* infoServer;
		PositionPredictor posePredictor;

		Quaternion globalRotation{};
		Quaternion localRotation{};

		// How many retries have been made before marking
		// the connection dead (assume max 180 retries or 3 seconds)
		int32_t eRetries = 0;

		// Message logging handler: bound to <Log>
		void LogMessage(const std::wstring& message, const int32_t& severity)
		{
			logEvent(*this, std::format(L"[{}] ", severity) + message);
		}
	};
}

namespace winrt::DeviceHandler::factory_implementation
{
	struct TrackingHandler : TrackingHandlerT<TrackingHandler, implementation::TrackingHandler>
	{
	};
}
