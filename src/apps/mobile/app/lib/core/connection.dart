import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'api_client.dart';
import 'credentials.dart';
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
  const Connected(this.api, this.server, {this.clockOffset = Duration.zero});

  final BakabaseApiClient api;
  final ServerInfo server;

  /// Kept from the handshake so pairing can be entered later without repeating it —
  /// a device that pairs voluntarily is already connected and has nothing to re-ask.
  final Duration clockOffset;
}

/// Reached and understood, and this device is about to ask for a key.
///
/// A state rather than a failure: nothing is wrong. It carries what the pairing screen
/// needs so that screen does not have to handshake again.
///
/// Two ways in, and [resumeTo] is what tells them apart. A server that refused this
/// device has nothing to go back to but the picker, so it is null. A user who chose to
/// pair from a working connection can change their mind, and that connection is still
/// good — throwing it away to make them find the server again would be a punishment
/// for looking.
class NeedsPairing extends ServerConnectionState {
  const NeedsPairing(this.baseUrl, this.server, this.clockOffset, {this.resumeTo});

  final String baseUrl;
  final ServerInfo server;

  /// Measured during the handshake that discovered the need to pair, so the very
  /// first signed request is already on the server's clock.
  final Duration clockOffset;

  /// Where backing out lands. Null when this device cannot use the server at all
  /// without pairing.
  final Connected? resumeTo;
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
  final CredentialStore _credentials = CredentialStore();

  /// How many times a denial has been recovered from without the user asking for
  /// anything in between. A phone whose clock is genuinely wrong would otherwise
  /// re-handshake on every refused request forever; two attempts is enough to absorb
  /// a drift the handshake can actually fix.
  int _recoveryAttempts = 0;

  static const int _maxRecoveryAttempts = 2;

  @override
  ServerConnectionState build() => const Disconnected();

  /// Reconnects to the most recently used server, if there is one.
  ///
  /// Called once at startup. Someone who uses one server — which is nearly everyone —
  /// should not have to pick it out of a list every time they open the app. It gives
  /// up quietly when there is nothing remembered, and a server that has moved or gone
  /// away simply surfaces its failure on the picker, which is where they would have
  /// landed anyway.
  Future<void> resumeLastServer() async {
    if (state is! Disconnected) {
      return;
    }

    final profiles = await _profiles.load();

    if (profiles.isEmpty || state is! Disconnected) {
      return;
    }

    await connect(profiles.first.baseUrl);
  }

  /// The whole connect handshake: reach the server, learn who it is, check
  /// protocol compatibility, remember it on success.
  Future<void> connect(String baseUrl, {bool userInitiated = true}) async {
    if (userInitiated) {
      _recoveryAttempts = 0;
    }

    state = Connecting(baseUrl);

    // The handshake is unsigned: server-info is reachable without a key, which is
    // what lets a device learn who it is talking to before deciding whether to pair.
    final sentAt = DateTime.now().toUtc();
    final ServerInfo info;
    try {
      info = await BakabaseApiClient(baseUrl).serverInfo();
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

    final offset = _measureOffset(info.serverTime, sentAt);
    final credentials = await _credentials.read(info.id);

    await _profiles.save(ServerProfile(
      id: info.id,
      name: info.name,
      baseUrl: baseUrl,
      lastConnectedAt: DateTime.now(),
      paired: credentials != null,
    ));

    final api = BakabaseApiClient(baseUrl,
        credentials: credentials, clockOffset: offset, onDenied: _onDenied);

    // Unpaired is fine on a server that does not require pairing — most of them, since
    // the switch is off by default. So rather than guessing from flags, ask for
    // something real and let the gate answer; only an Unauthenticated refusal means
    // this device actually has to pair.
    if (credentials == null && info.pairingSupported) {
      try {
        await api.mediaLibraries();
      } on ApiException catch (e) {
        if (e.denial == RemoteAccessDenial.unauthenticated) {
          state = NeedsPairing(baseUrl, info, offset);
          return;
        }
      }
    }

    state = Connected(api, info, clockOffset: offset);
  }

  /// Enters pairing because the user asked to, not because the server refused.
  ///
  /// The refusal route only fires on a server with RequirePairing switched on, which
  /// is off by default — so without this a phone stays anonymous forever on the
  /// servers most people run, while the desktop client pairs on first sight. Pairing
  /// is also what unlocks the device list and the paths outside the libraries, and
  /// neither of those announces itself by failing.
  void startPairing() {
    if (state case final Connected current) {
      state = NeedsPairing(
        current.api.baseUrl,
        current.server,
        current.clockOffset,
        resumeTo: current,
      );
    }
  }

  /// Backs out of pairing, to wherever there is to back out to.
  void cancelPairing() {
    if (state case NeedsPairing(resumeTo: final resume)) {
      state = resume ?? const Disconnected();
    }
  }

  /// Stores the credentials a pairing produced and connects with them.
  Future<void> completePairing(
      String baseUrl, ServerInfo server, Duration clockOffset, DeviceCredentials credentials) async {
    await _credentials.write(server.id, credentials);
    await _profiles.setPaired(server.id, true);
    _recoveryAttempts = 0;

    state = Connected(
      BakabaseApiClient(baseUrl,
          credentials: credentials, clockOffset: clockOffset, onDenied: _onDenied),
      server,
      clockOffset: clockOffset,
    );
  }

  /// Reacts to the two refusals that mean this device's credentials stopped working.
  ///
  /// They look alike from a request's point of view and need opposite fixes, which is
  /// why the server distinguishes them and why this does too. A revoked device has to
  /// pair again — its key is gone from the server and every further request it signs
  /// can only be refused. An expired signature is not a pairing problem at all: the
  /// clock drifted, and re-running the handshake re-measures the offset. Telling
  /// someone their pairing broke when their clock is off sends them to the wrong fix.
  ///
  /// Without this, a phone holding a dead key sat in [Connected] showing a raw English
  /// refusal in the middle of the library, with nothing to tap.
  void _onDenied(RemoteAccessDenial denial) {
    if (_recoveryAttempts >= _maxRecoveryAttempts || state is! Connected) {
      return;
    }

    _recoveryAttempts++;
    unawaited(_recover(denial));
  }

  Future<void> _recover(RemoteAccessDenial denial) async {
    if (state case final Connected current) {
      switch (denial) {
        case RemoteAccessDenial.deviceRevoked:
          await _credentials.delete(current.server.id);
          await _profiles.setPaired(current.server.id, false);

          if (state case Connected(server: final s) when s.id == current.server.id) {
            state = NeedsPairing(current.api.baseUrl, current.server, current.clockOffset);
          }
        case RemoteAccessDenial.signatureExpired:
          await connect(current.api.baseUrl, userInitiated: false);
        default:
          return;
      }
    }
  }

  /// Forgets a server, and its key with it — leaving the key behind would strand a
  /// secret in the keystore for a server the user meant to be rid of.
  Future<void> forget(String serverId) async {
    await _credentials.delete(serverId);
    await _profiles.remove(serverId);

    if (state case Connected(server: final s) when s.id == serverId) {
      state = const Disconnected();
    }
  }

  /// How far this device's clock sits from the server's.
  ///
  /// Half the round trip is charged to the outbound leg, which is the usual
  /// approximation and is far below the five minutes of skew the server allows. Zero
  /// when the server did not report a time — an older one — which leaves this device
  /// signing with its own clock, exactly as it would have before.
  static Duration _measureOffset(DateTime? serverTime, DateTime sentAt) {
    if (serverTime == null) {
      return Duration.zero;
    }

    final roundTrip = DateTime.now().toUtc().difference(sentAt);

    if (roundTrip.isNegative) {
      return Duration.zero;
    }

    return serverTime.difference(sentAt.add(roundTrip ~/ 2));
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
