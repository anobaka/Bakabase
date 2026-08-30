import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'api_client.dart';
import 'models.dart';
import 'server_profiles.dart';

/// Protocol versions this build of the app can talk to. Compared against the
/// server's protocolVersion during the handshake.
const int minSupportedProtocol = 1;
const int maxSupportedProtocol = 1;

sealed class ServerConnectionState {
  const ServerConnectionState();
}

class Disconnected extends ServerConnectionState {
  const Disconnected();
}

class Connecting extends ServerConnectionState {
  const Connecting(this.baseUrl);

  final String baseUrl;
}

class Connected extends ServerConnectionState {
  const Connected(this.api, this.server);

  final BakabaseApiClient api;
  final ServerInfo server;
}

/// Why a connect attempt failed — as data, so the UI layer owns the wording
/// (and its translation).
enum ConnectionFailureKind { network, protocolTooNew, protocolTooOld }

class ConnectionFailed extends ServerConnectionState {
  const ConnectionFailed(this.baseUrl, this.kind, this.detail, {this.denial});

  final String baseUrl;
  final ConnectionFailureKind kind;

  /// The raw error message for [ConnectionFailureKind.network]; the server's
  /// protocol version for the protocol kinds.
  final String detail;
  final RemoteAccessDenial? denial;
}

class ConnectionController extends Notifier<ServerConnectionState> {
  final ServerProfileStore _profiles = ServerProfileStore();

  @override
  ServerConnectionState build() => const Disconnected();

  /// The whole connect handshake: reach the server, learn who it is, check
  /// protocol compatibility, remember it on success.
  Future<void> connect(String baseUrl) async {
    state = Connecting(baseUrl);

    final api = BakabaseApiClient(baseUrl);
    final ServerInfo info;
    try {
      info = await api.serverInfo();
    } on ApiException catch (e) {
      state = ConnectionFailed(baseUrl, ConnectionFailureKind.network, e.message,
          denial: e.denial);
      return;
    }

    if (info.protocolVersion > maxSupportedProtocol) {
      state = ConnectionFailed(
          baseUrl, ConnectionFailureKind.protocolTooNew, '${info.protocolVersion}');
      return;
    }
    if (info.protocolVersion < minSupportedProtocol) {
      state = ConnectionFailed(
          baseUrl, ConnectionFailureKind.protocolTooOld, '${info.protocolVersion}');
      return;
    }

    await _profiles.save(ServerProfile(
      id: info.id,
      name: info.name,
      baseUrl: baseUrl,
      lastConnectedAt: DateTime.now(),
    ));

    state = Connected(api, info);
  }

  void disconnect() {
    state = const Disconnected();
  }
}

final connectionProvider = NotifierProvider<ConnectionController, ServerConnectionState>(
  ConnectionController.new,
);

final serverProfilesProvider = FutureProvider<List<ServerProfile>>((ref) {
  // Re-reads whenever the connection changes, so a fresh connect reorders the
  // remembered list.
  ref.watch(connectionProvider);
  return ServerProfileStore().load();
});
