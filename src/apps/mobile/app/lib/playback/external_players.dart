import 'dart:io';

import 'package:android_intent_plus/android_intent.dart';
import 'package:flutter/foundation.dart';
import 'package:url_launcher/url_launcher.dart';

/// Hands an HTTP stream to a native player installed on this device — the
/// mobile port of the web's `playerSchemes.ts`. The in-app player covers most
/// files, but a user's favorite player (or a codec libmpv lacks a license
/// for) is one tap away, pulling the same `/file/raw` bytes.
///
/// Launch failures are inherent: no platform can tell whether the target app
/// is installed before trying. Callers surface a "not installed?" hint on
/// false.
class ExternalPlayer {
  const ExternalPlayer({required this.id, required this.name, this.androidPackage});

  final String id;

  /// Product names, shown as-is (not translated).
  final String name;

  /// Android package to pin the intent to; null lets the system chooser pick.
  final String? androidPackage;

  static const androidPlayers = [
    ExternalPlayer(id: 'vlc', name: 'VLC', androidPackage: 'org.videolan.vlc'),
    ExternalPlayer(id: 'mx', name: 'MX Player', androidPackage: 'com.mxtech.videoplayer.ad'),
    ExternalPlayer(id: 'mx-pro', name: 'MX Player Pro', androidPackage: 'com.mxtech.videoplayer.pro'),
    ExternalPlayer(id: 'mpv', name: 'mpv', androidPackage: 'is.xyz.mpv'),
    ExternalPlayer(id: 'chooser', name: 'Other player…'),
  ];

  static const iosPlayers = [
    ExternalPlayer(id: 'vlc', name: 'VLC'),
    ExternalPlayer(id: 'infuse', name: 'Infuse'),
    ExternalPlayer(id: 'nplayer', name: 'nPlayer'),
    ExternalPlayer(id: 'senplayer', name: 'SenPlayer'),
  ];

  static List<ExternalPlayer> forThisDevice() {
    if (kIsWeb) {
      return const [];
    }
    if (Platform.isAndroid) {
      return androidPlayers;
    }
    if (Platform.isIOS) {
      return iosPlayers;
    }
    return const [];
  }

  /// iOS deep link for [id], mirroring the schemes documented in
  /// playerSchemes.ts. Pure, for tests.
  static String iosSchemeUrl(String id, String streamUrl) {
    final encoded = Uri.encodeComponent(streamUrl);
    return switch (id) {
      'vlc' => 'vlc-x-callback://x-callback-url/stream?url=$encoded',
      'infuse' => 'infuse://x-callback-url/play?url=$encoded',
      // nPlayer rewrites the scheme rather than taking a url parameter.
      'nplayer' => streamUrl.replaceFirst(RegExp('^http'), 'nplayer-http'),
      'senplayer' => 'SenPlayer://x-callback-url/play?url=$encoded',
      _ => streamUrl,
    };
  }

  /// Launches this player with [streamUrl]. Returns false when the hand-off
  /// visibly failed (usually: the app is not installed).
  Future<bool> launch(String streamUrl) async {
    if (Platform.isAndroid) {
      // A real ACTION_VIEW intent, not the browser's `intent://` URL trick —
      // inside our own app we can pin the package directly.
      final intent = AndroidIntent(
        action: 'action_view',
        data: streamUrl,
        type: 'video/*',
        package: androidPackage,
      );
      try {
        await intent.launch();
        return true;
      } on Exception {
        return false;
      }
    }

    try {
      return await launchUrl(
        Uri.parse(iosSchemeUrl(id, streamUrl)),
        mode: LaunchMode.externalApplication,
      );
    } on Exception {
      return false;
    }
  }
}
