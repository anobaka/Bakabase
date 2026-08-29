/// One Bakabase install seen on the network, whatever channel it came from.
/// [id] is the server's persistent identity; the address is transient.
class DiscoveredServer {
  const DiscoveredServer({
    required this.id,
    required this.name,
    required this.host,
    required this.port,
    this.appVersion = '',
    this.protocolVersion = 0,
  });

  final String id;
  final String name;
  final String host;
  final int port;
  final String appVersion;
  final int protocolVersion;

  String get baseUrl => 'http://$host:$port';

  @override
  String toString() => '$name ($baseUrl)';
}
