// from https://stackoverflow.com/questions/14665543/how-do-i-receive-udp-packets-with-winsock-in-c

#pragma once

#define  _WINSOCK_DEPRECATED_NO_WARNINGS
#include <functional>
#include <WinSock2.h>
#include <WS2tcpip.h>
#include <system_error>
#include <string>
#include <iostream>

#pragma comment(lib, "Ws2_32.lib")

class WSASession
{
public:
	WSASession()
	{
		const int ret = WSAStartup(MAKEWORD(2, 2), &data);
		if (ret != 0)
			throw std::system_error(WSAGetLastError(), std::system_category(), "WSAStartup Failed");
	}

	~WSASession()
	{
		WSACleanup();
	}

private:
	WSAData data;
};

class UDPSocket
{
	std::function<void(std::wstring, int32_t)> Log;

public:
	UDPSocket(std::function<void(std::wstring, int32_t)> loggerFunction) :
		Log(loggerFunction)
	{
		sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);

		unsigned long ul = 1;
		ioctlsocket(sock, FIONBIO, &ul);

		if (sock == INVALID_SOCKET)
			throw std::system_error(WSAGetLastError(), std::system_category(), "Error opening socket");
	}

	~UDPSocket()
	{
		closesocket(sock);
	}

	void SendTo(const std::string& address, unsigned short port, const char* buffer, int len, int flags = 0)
	{
		sockaddr_in add;
		add.sin_family = AF_INET;
		add.sin_addr.s_addr = inet_addr(address.c_str());
		add.sin_port = htons(port);
		const int ret = sendto(sock, buffer, len, flags, reinterpret_cast<SOCKADDR*>(&add), sizeof(add));
		if (ret < 0)
			throw std::system_error(WSAGetLastError(), std::system_category(), "sendto failed");
	}

	void SendTo(sockaddr_in& address, const char* buffer, int len, int flags = 0)
	{
		const int ret = sendto(sock, buffer, len, flags, reinterpret_cast<SOCKADDR*>(&address), sizeof(address));
		if (ret < 0)
			throw std::system_error(WSAGetLastError(), std::system_category(), "sendto failed");
	}

	bool RecvFrom(char* buffer, int len, SOCKADDR* from, int flags = 0)
	{
		int size = sizeof(sockaddr_in); // reinterpret_cast<SOCKADDR*>(&from)
		const int ret = recvfrom(sock, buffer, len, flags, from, &size);
		if (ret == WSAEWOULDBLOCK)
			return false;
		if (ret < 0)
		{
			const int err = WSAGetLastError();
			if (err == WSAEWOULDBLOCK)
			{
				return false;
			}
			throw std::system_error(err, std::system_category(), "recvfrom failed");
		}

		// make the buffer zero terminated
		buffer[ret] = 0;
		return true;
	}

	bool Bind(uint32_t* port)
	{
		sockaddr_in add;
		add.sin_family = AF_INET;
		add.sin_addr.s_addr = htonl(INADDR_ANY);

		add.sin_port = htons(*port);
		const int ret = bind(sock, reinterpret_cast<SOCKADDR*>(&add), sizeof(add));

		if (ret < 0)
		{
			Log(std::format(L"OWO Device: Port {} is already taken, giving up!", *port), 2);
			return false;
		}

		Log(std::format(L"Port bind successful at {}!", *port), 0);
		return true; // We're good
	}

	void Bind(unsigned short port)
	{
		sockaddr_in add;
		add.sin_family = AF_INET;
		add.sin_addr.s_addr = htonl(INADDR_ANY);
		add.sin_port = htons(port);

		const int ret = bind(sock, reinterpret_cast<SOCKADDR*>(&add), sizeof(add));
		if (ret < 0)
			throw std::system_error(WSAGetLastError(), std::system_category(), "Bind failed");
	}

	//private:
	SOCKET sock;
};
