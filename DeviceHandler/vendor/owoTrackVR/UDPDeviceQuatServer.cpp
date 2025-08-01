#include "pch.h"

#include "UDPDeviceQuatServer.h"
#include "ByteBuffer.h"

#include <ctime>

using namespace bb;

void UDPDeviceQuatServer::send_heartbeat()
{
	hb_accum += 1;

	if (hb_accum > 200)
	{
		hb_accum = 0;

		if (!isConnectionAlive())
			return;

		ByteBuffer buff(sizeof(int) * 2);
		buff.putInt(1);
		buff.putInt(0);

		send_bytebuffer(buff);
	}
}

void UDPDeviceQuatServer::send_bytebuffer(ByteBuffer& b)
{
	auto buff_c = static_cast<uint8_t*>(malloc(b.size()));
	b.getBytes(buff_c, b.size());

	Socket.SendTo(client, (char*)buff_c, b.size());

	free(buff_c);
}

UDPDeviceQuatServer::UDPDeviceQuatServer(uint32_t* portno_v,
                                         std::function<void(std::wstring, int32_t)> loggerFunction) :
	NetworkedDeviceQuatServer(), Log(loggerFunction), Socket(loggerFunction)
{
	buffer = static_cast<char*>(malloc(sizeof(char) * MAX_MSG_SIZE));

	portno = portno_v;

	client = {0};
}


void UDPDeviceQuatServer::startListening(bool& _ret)
{
	_ret = Socket.Bind(portno);
}

bool UDPDeviceQuatServer::more_data_exists__read()
{
	// read header
	curr_time = static_cast<unsigned long long>(std::time(nullptr));

	const bool is_recv = Socket.RecvFrom(buffer, MAX_MSG_SIZE, reinterpret_cast<SOCKADDR*>(&client));
	if (!is_recv) return false;

	const message_header_type_t msg_type = convert_chars<message_header_type_t>((unsigned char*)buffer);


	last_contact_time = curr_time;
	connectionIsDead = false;

	switch (msg_type)
	{
	case MSG_HEARTBEAT:
		return true;
	case MSG_ROTATION:
		handle_rotation_packet((unsigned char*)buffer);
		return true;
	case MSG_GYRO:
		handle_gyro_packet((unsigned char*)buffer);
		return true;
	case MSG_ACCELEROMETER:
		handle_accel_packet((unsigned char*)buffer);
		return true;
	case MSG_HANDSHAKE:
		Socket.SendTo(client, buff_hello, buff_hello_len);
		return true;
	default:
		return true;
	}
	return true;
}

void UDPDeviceQuatServer::tick()
{
	send_heartbeat();
	while (more_data_exists__read())
	{
	}
}

bool UDPDeviceQuatServer::isConnectionAlive()
{
	// cache
	if (connectionIsDead)
		return false;

	if ((curr_time - last_contact_time) > 2)
		connectionIsDead = true;

	return !connectionIsDead;
}

void UDPDeviceQuatServer::buzz(float duration_s, float frequency, float amplitude)
{
	ByteBuffer buff(sizeof(int) + sizeof(float) * 3);
	buff.putInt(2);
	buff.putFloat(duration_s);
	buff.putFloat(frequency);
	buff.putFloat(amplitude);

	send_bytebuffer(buff);
}

int UDPDeviceQuatServer::get_port()
{
	return *portno;
}
