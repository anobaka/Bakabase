// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'Bakabase';

  @override
  String get connectTitle => 'Connect to Bakabase';

  @override
  String get connecting => 'Connecting…';

  @override
  String get onThisNetwork => 'On this network';

  @override
  String get discoveryHint =>
      'Searching… Make sure Bakabase is running with remote access enabled, and that this device is on the same network. On iOS, allow local network access when asked.';

  @override
  String get remembered => 'Remembered';

  @override
  String get rememberedEmpty => 'Servers you connect to are remembered here.';

  @override
  String get byAddress => 'By address';

  @override
  String get connect => 'Connect';

  @override
  String couldNotConnect(String url) {
    return 'Could not connect to $url';
  }

  @override
  String get remoteAccessDisabledHint =>
      'Remote access is turned off. Enable it in Bakabase on the host machine (Settings → Remote access).';

  @override
  String protocolTooNew(String version) {
    return 'Server speaks protocol v$version; this app is too old for it. Update the app.';
  }

  @override
  String protocolTooOld(String version) {
    return 'Server speaks protocol v$version; this app needs a newer server. Update Bakabase on the host.';
  }

  @override
  String get searchHint => 'Search resources';

  @override
  String get allLibraries => 'All';

  @override
  String get sortTooltip => 'Sort';

  @override
  String get sortAddDt => 'Recently added';

  @override
  String get sortPlayedAt => 'Recently played';

  @override
  String get sortFileModifyDt => 'File modified';

  @override
  String get sortFilename => 'Filename';

  @override
  String get ascending => 'Ascending ↑';

  @override
  String get descending => 'Descending ↓';

  @override
  String get playHistoryTooltip => 'Play history';

  @override
  String get switchServerTooltip => 'Switch server';

  @override
  String get openWebUiTooltip => 'Open full web UI';

  @override
  String get playHere => 'Play here';

  @override
  String get builtInPlayer => 'Built-in player (mpv)';

  @override
  String get copyStreamLink => 'Copy stream link';

  @override
  String get streamLinkCopied => 'Stream link copied — paste it into a player';

  @override
  String couldNotOpenPlayer(String name) {
    return 'Could not open $name — is it installed?';
  }

  @override
  String get otherPlayer => 'Other player…';

  @override
  String readPages(int count) {
    return 'Read ($count pages)';
  }

  @override
  String get playSection => 'Play';

  @override
  String get otherFiles => 'Other files';

  @override
  String get noPlayableFiles =>
      'No playable files were found in this resource.';

  @override
  String get playHistory => 'Play history';

  @override
  String removedResource(int id) {
    return 'Resource #$id (removed)';
  }
}
