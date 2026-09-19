import 'package:bakabase_mobile/core/api_client.dart';
import 'package:bakabase_mobile/core/connection.dart';
import 'package:bakabase_mobile/core/models.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Entering and leaving pairing on purpose.
///
/// Only the transitions are exercised here, not the stores: pairing voluntarily and
/// backing out of it touch neither the keystore nor preferences, which is what makes
/// them testable without a platform channel. The routes that do — completing a pairing,
/// recovering from a revoked device — are covered by the widget tests that run on a
/// device.
class _TestableConnection extends ConnectionController {
  /// The one thing a test needs that the real flow gets from a handshake.
  void enter(ServerConnectionState value) => state = value;
}

void main() {
  final provider =
      NotifierProvider<_TestableConnection, ServerConnectionState>(_TestableConnection.new);

  const server = ServerInfo(
    id: 'server-1',
    name: 'Desk',
    appVersion: '2.4.0',
    protocolVersion: 1,
    pairingSupported: true,
  );

  const offset = Duration(seconds: 3);

  late ProviderContainer container;
  late _TestableConnection connection;

  setUp(() {
    container = ProviderContainer();
    addTearDown(container.dispose);
    connection = container.read(provider.notifier);
  });

  Connected connected() =>
      Connected(BakabaseApiClient('http://host:34567'), server, clockOffset: offset);

  group('pairing on purpose', () {
    test('a connected device can ask to pair without being refused first', () {
      // The refusal route only fires on a server with RequirePairing on, which is off
      // by default — so without this the phone can never pair with the servers most
      // people actually run.
      connection.enter(connected());
      connection.startPairing();

      final state = container.read(provider);

      expect(state, isA<NeedsPairing>());
      expect((state as NeedsPairing).server.id, 'server-1');
      expect(state.baseUrl, 'http://host:34567');
    });

    test('carries the handshake clock offset through, rather than re-measuring', () {
      connection.enter(connected());
      connection.startPairing();

      expect((container.read(provider) as NeedsPairing).clockOffset, offset);
    });

    test('does nothing from a state that has no server to pair with', () {
      connection.enter(const Disconnected());
      connection.startPairing();

      expect(container.read(provider), isA<Disconnected>());
    });
  });

  group('backing out', () {
    test('a voluntary pairing returns to the connection it came from', () {
      // Throwing away a working connection because someone opened pairing and changed
      // their mind would be a punishment for looking.
      final before = connected();

      connection.enter(before);
      connection.startPairing();
      connection.cancelPairing();

      expect(container.read(provider), same(before));
    });

    test('a demanded pairing has nowhere to go but the picker', () {
      connection.enter(const NeedsPairing('http://host:34567', server, offset));
      connection.cancelPairing();

      expect(container.read(provider), isA<Disconnected>());
    });

    test('is a no-op when pairing is not what is on screen', () {
      final current = connected();

      connection.enter(current);
      connection.cancelPairing();

      expect(container.read(provider), same(current));
    });
  });
}
