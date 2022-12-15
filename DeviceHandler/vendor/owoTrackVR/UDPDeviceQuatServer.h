#pragma once

#include "NetworkedDeviceQuatServer.h"
#include "Network.h"
#include "ByteBuffer.h"

using namespace bb;

class UDPDeviceQuatServer : public NetworkedDeviceQuatServer
{
private:
	std::function<void(std::wstring, int32_t)> Log;

	uint32_t* portno; // Port number pointer
	WSASession Session;
	UDPSocket Socket;

	sockaddr_in client;

	void send_heartbeat();

	char* buffer;

	bool more_data_exists__read();

	unsigned long long last_contact_time = 0;
	unsigned long long curr_time = 0;

	bool connectionIsDead = false;

	int hb_accum;

	void send_bytebuffer(ByteBuffer& b);

public:
	UDPDeviceQuatServer(uint32_t* portno_v, std::function<void(std::wstring, int32_t)> loggerFunction);

	void startListening(bool& _ret) override;
	void tick() override;

	bool isConnectionAlive() override;

	void buzz(float duration_s, float frequency, float amplitude) override;

	int get_port() override;
};
