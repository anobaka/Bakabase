import 'dart:convert';

import 'discovered_server.dart';

/// The client half of the discovery wire formats — kept pure so the contract
/// with the server's DiscoveryProtocol/MdnsAdvertisement is unit-testable.
/// Constants mirror RemoteAccessProtocol on the C# side.
class DiscoveryCodec {
  static const String mdnsServiceType = '_bakabase._tcp';
  static const int probePort = 33333;
  static const String probeRequest = 'BAKABASE_DISCOVER_V1';
  static const String probeResponsePrefix = 'BAKABASE_HERE_V1 ';

  static List<int> buildProbeRequest() => utf8.encode(probeRequest);

  /// Parses a probe reply. [senderHost] is the datagram's source address —
  /// the payload deliberately carries no address, since the server cannot know
  /// which of its interfaces the client can route to.
  static DiscoveredServer? tryParseProbeResponse(List<int> datagram, String senderHost) {
    final String text;
    try {
      text = utf8.decode(datagram);
    } on FormatException {
      return null;
    }

    if (!text.startsWith(probeResponsePrefix)) {
      return null;
    }

    final dynamic json;
    try {
      json = jsonDecode(text.substring(probeResponsePrefix.length));
    } on FormatException {
      return null;
    }

    if (json is! Map<String, dynamic>) {
      return null;
    }

    return _fromFacts(json, senderHost);
  }

  /// Builds a server from mDNS TXT attributes plus the resolved host/port.
  /// The SRV port wins over the TXT `port` entry when both are present.
  static DiscoveredServer? fromTxt(Map<String, String> txt, String host, int srvPort) {
    final facts = <String, dynamic>{...txt};
    final server = _fromFacts(facts, host);
    if (server == null) {
      return null;
    }
    return DiscoveredServer(
      id: server.id,
      name: server.name,
      host: host,
      port: srvPort > 0 ? srvPort : server.port,
      appVersion: server.appVersion,
      protocolVersion: server.protocolVersion,
    );
  }

  static DiscoveredServer? _fromFacts(Map<String, dynamic> facts, String host) {
    final id = facts['id']?.toString();
    final port = int.tryParse(facts['port']?.toString() ?? '') ?? 0;

    // Without an identity the entry cannot be deduplicated or remembered;
    // without a port there is nothing to connect to.
    if (id == null || id.isEmpty || port <= 0) {
      return null;
    }

    return DiscoveredServer(
      id: id,
      name: facts['name']?.toString() ?? 'Bakabase',
      host: host,
      port: port,
      appVersion: facts['ver']?.toString() ?? '',
      protocolVersion: int.tryParse(facts['proto']?.toString() ?? '') ?? 0,
    );
  }
}
