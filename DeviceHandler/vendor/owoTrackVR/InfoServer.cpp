#include "pch.h"
#include "InfoServer.h"

bool InfoServer::respond_to_all_requests()
{
	sockaddr_in addr;
	const bool is_recv = Socket.RecvFrom(buff, MAX_BUFF_SIZE, reinterpret_cast<SOCKADDR*>(&addr));
	if (!is_recv) return false;

	if (strcmp(buff, "DISCOVERY\0") == 0)
	{
		Socket.SendTo(addr, response_info.c_str(), response_info.length());
	}
}

InfoServer::InfoServer(bool& _ret, std::function<void(std::wstring, int32_t)> loggerFunction) :
	Log(loggerFunction), Socket(loggerFunction)
{
	buff = static_cast<char*>(malloc(MAX_BUFF_SIZE));
	_ret = Socket.Bind(&INFO_PORT);
}

void InfoServer::add_tracker()
{
	response_info = std::to_string(port_no) + ":Default\n";
}

void InfoServer::tick()
{
	while (respond_to_all_requests())
	{
	}
}
