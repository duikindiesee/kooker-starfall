"""Integration-test launcher. Denies outbound socket connections in this process."""
import json
import socket
import server


def denied(*_args, **_kwargs):
    raise OSError('Outbound networking is disabled in the Starfall memory proof')


socket.socket.connect = denied
socket.socket.connect_ex = denied
socket.create_connection = denied
socket.getaddrinfo = denied
socket.getfqdn = lambda *_: 'localhost'
print(json.dumps({'offline_guard': 'outbound-connect-and-dns-denied'}), flush=True)
server.main()
