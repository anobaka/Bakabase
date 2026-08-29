import 'dart:convert';

import 'package:bakabase_mobile/discovery/discovery_codec.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('probe request', () {
    test('is the agreed magic string', () {
      expect(utf8.decode(DiscoveryCodec.buildProbeRequest()), 'BAKABASE_DISCOVER_V1');
    });
  });

  group('probe response parsing', () {
    List<int> response(Map<String, dynamic> payload) =>
        utf8.encode('BAKABASE_HERE_V1 ${jsonEncode(payload)}');

    test('parses the server facts, taking the address from the sender', () {
      final server = DiscoveryCodec.tryParseProbeResponse(
        response({
          'id': 'abc123',
          'name': 'My-PC',
          'port': 34567,
          'ver': '2.4.0-beta',
          'proto': 1,
        }),
        '192.168.1.5',
      );

      expect(server, isNotNull);
      expect(server!.id, 'abc123');
      expect(server.name, 'My-PC');
      expect(server.host, '192.168.1.5');
      expect(server.port, 34567);
      expect(server.appVersion, '2.4.0-beta');
      expect(server.protocolVersion, 1);
      expect(server.baseUrl, 'http://192.168.1.5:34567');
    });

    test('rejects anything without the prefix, or malformed', () {
      expect(DiscoveryCodec.tryParseProbeResponse(utf8.encode('hello'), 'h'), isNull);
      expect(
        DiscoveryCodec.tryParseProbeResponse(utf8.encode('BAKABASE_HERE_V1 not-json'), 'h'),
        isNull,
      );
      expect(DiscoveryCodec.tryParseProbeResponse([0xFF, 0xFE], 'h'), isNull);
    });

    test('rejects payloads missing an id or a usable port', () {
      expect(
        DiscoveryCodec.tryParseProbeResponse(response({'name': 'x', 'port': 1}), 'h'),
        isNull,
      );
      expect(
        DiscoveryCodec.tryParseProbeResponse(response({'id': 'a', 'port': 0}), 'h'),
        isNull,
      );
    });
  });

  group('mDNS TXT parsing', () {
    test('parses TXT facts, preferring the SRV port', () {
      final server = DiscoveryCodec.fromTxt(
        {'id': 'abc', 'name': 'NAS', 'port': '11111', 'ver': '2.4.0', 'proto': '1'},
        '10.0.0.2',
        34567,
      );

      expect(server, isNotNull);
      expect(server!.port, 34567);
      expect(server.host, '10.0.0.2');
      expect(server.name, 'NAS');
    });

    test('falls back to the TXT port when SRV has none', () {
      final server = DiscoveryCodec.fromTxt(
        {'id': 'abc', 'port': '11111'},
        '10.0.0.2',
        0,
      );

      expect(server!.port, 11111);
    });

    test('rejects TXT without an id', () {
      expect(DiscoveryCodec.fromTxt({'port': '1'}, 'h', 1), isNull);
    });
  });
}
