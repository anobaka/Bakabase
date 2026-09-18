import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_zh.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('zh'),
  ];

  /// No description provided for @appTitle.
  ///
  /// In en, this message translates to:
  /// **'Bakabase'**
  String get appTitle;

  /// No description provided for @connectTitle.
  ///
  /// In en, this message translates to:
  /// **'Connect to Bakabase'**
  String get connectTitle;

  /// No description provided for @connecting.
  ///
  /// In en, this message translates to:
  /// **'Connecting…'**
  String get connecting;

  /// No description provided for @onThisNetwork.
  ///
  /// In en, this message translates to:
  /// **'On this network'**
  String get onThisNetwork;

  /// No description provided for @discoveryHint.
  ///
  /// In en, this message translates to:
  /// **'Searching… Make sure Bakabase is running with remote access enabled, and that this device is on the same network. On iOS, allow local network access when asked.'**
  String get discoveryHint;

  /// No description provided for @remembered.
  ///
  /// In en, this message translates to:
  /// **'Remembered'**
  String get remembered;

  /// No description provided for @rememberedEmpty.
  ///
  /// In en, this message translates to:
  /// **'Servers you connect to are remembered here.'**
  String get rememberedEmpty;

  /// No description provided for @byAddress.
  ///
  /// In en, this message translates to:
  /// **'By address'**
  String get byAddress;

  /// No description provided for @connect.
  ///
  /// In en, this message translates to:
  /// **'Connect'**
  String get connect;

  /// No description provided for @couldNotConnect.
  ///
  /// In en, this message translates to:
  /// **'Could not connect to {url}'**
  String couldNotConnect(String url);

  /// No description provided for @remoteAccessDisabledHint.
  ///
  /// In en, this message translates to:
  /// **'Remote access is turned off. Enable it in Bakabase on the host machine (Settings → Remote access).'**
  String get remoteAccessDisabledHint;

  /// No description provided for @protocolTooNew.
  ///
  /// In en, this message translates to:
  /// **'Server speaks protocol v{version}; this app is too old for it. Update the app.'**
  String protocolTooNew(String version);

  /// No description provided for @protocolTooOld.
  ///
  /// In en, this message translates to:
  /// **'Server speaks protocol v{version}; this app needs a newer server. Update Bakabase on the host.'**
  String protocolTooOld(String version);

  /// No description provided for @searchHint.
  ///
  /// In en, this message translates to:
  /// **'Search resources'**
  String get searchHint;

  /// No description provided for @allLibraries.
  ///
  /// In en, this message translates to:
  /// **'All'**
  String get allLibraries;

  /// No description provided for @sortTooltip.
  ///
  /// In en, this message translates to:
  /// **'Sort'**
  String get sortTooltip;

  /// No description provided for @sortAddDt.
  ///
  /// In en, this message translates to:
  /// **'Recently added'**
  String get sortAddDt;

  /// No description provided for @sortPlayedAt.
  ///
  /// In en, this message translates to:
  /// **'Recently played'**
  String get sortPlayedAt;

  /// No description provided for @sortFileModifyDt.
  ///
  /// In en, this message translates to:
  /// **'File modified'**
  String get sortFileModifyDt;

  /// No description provided for @sortFilename.
  ///
  /// In en, this message translates to:
  /// **'Filename'**
  String get sortFilename;

  /// No description provided for @ascending.
  ///
  /// In en, this message translates to:
  /// **'Ascending ↑'**
  String get ascending;

  /// No description provided for @descending.
  ///
  /// In en, this message translates to:
  /// **'Descending ↓'**
  String get descending;

  /// No description provided for @playHistoryTooltip.
  ///
  /// In en, this message translates to:
  /// **'Play history'**
  String get playHistoryTooltip;

  /// No description provided for @switchServerTooltip.
  ///
  /// In en, this message translates to:
  /// **'Switch server'**
  String get switchServerTooltip;

  /// No description provided for @openWebUiTooltip.
  ///
  /// In en, this message translates to:
  /// **'Open full web UI'**
  String get openWebUiTooltip;

  /// No description provided for @playHere.
  ///
  /// In en, this message translates to:
  /// **'Play here'**
  String get playHere;

  /// No description provided for @builtInPlayer.
  ///
  /// In en, this message translates to:
  /// **'Built-in player (mpv)'**
  String get builtInPlayer;

  /// No description provided for @copyStreamLink.
  ///
  /// In en, this message translates to:
  /// **'Copy stream link'**
  String get copyStreamLink;

  /// No description provided for @streamLinkCopied.
  ///
  /// In en, this message translates to:
  /// **'Stream link copied — paste it into a player'**
  String get streamLinkCopied;

  /// No description provided for @couldNotOpenPlayer.
  ///
  /// In en, this message translates to:
  /// **'Could not open {name} — is it installed?'**
  String couldNotOpenPlayer(String name);

  /// No description provided for @otherPlayer.
  ///
  /// In en, this message translates to:
  /// **'Other player…'**
  String get otherPlayer;

  /// No description provided for @readPages.
  ///
  /// In en, this message translates to:
  /// **'Read ({count} pages)'**
  String readPages(int count);

  /// No description provided for @playSection.
  ///
  /// In en, this message translates to:
  /// **'Play'**
  String get playSection;

  /// No description provided for @otherFiles.
  ///
  /// In en, this message translates to:
  /// **'Other files'**
  String get otherFiles;

  /// No description provided for @noPlayableFiles.
  ///
  /// In en, this message translates to:
  /// **'No playable files were found in this resource.'**
  String get noPlayableFiles;

  /// No description provided for @playHistory.
  ///
  /// In en, this message translates to:
  /// **'Play history'**
  String get playHistory;

  /// No description provided for @removedResource.
  ///
  /// In en, this message translates to:
  /// **'Resource #{id} (removed)'**
  String removedResource(int id);

  /// No description provided for @pairTitle.
  ///
  /// In en, this message translates to:
  /// **'Pair this device'**
  String get pairTitle;

  /// No description provided for @pairIntro.
  ///
  /// In en, this message translates to:
  /// **'This server only serves devices it knows. Read the code from Bakabase on the server, or ask someone at a paired device to approve this one.'**
  String get pairIntro;

  /// No description provided for @pairCodeLabel.
  ///
  /// In en, this message translates to:
  /// **'6-digit code'**
  String get pairCodeLabel;

  /// No description provided for @pairSubmit.
  ///
  /// In en, this message translates to:
  /// **'Pair'**
  String get pairSubmit;

  /// No description provided for @pairAskInstead.
  ///
  /// In en, this message translates to:
  /// **'Ask for approval instead'**
  String get pairAskInstead;

  /// No description provided for @pairWaiting.
  ///
  /// In en, this message translates to:
  /// **'Waiting for someone to approve this device…'**
  String get pairWaiting;

  /// No description provided for @pairCodeRejected.
  ///
  /// In en, this message translates to:
  /// **'That code was wrong or has expired. Read a fresh one from the server.'**
  String get pairCodeRejected;

  /// No description provided for @pairRequestRejected.
  ///
  /// In en, this message translates to:
  /// **'The request was turned down, or waited too long.'**
  String get pairRequestRejected;

  /// No description provided for @pairTooManyAttempts.
  ///
  /// In en, this message translates to:
  /// **'The server is not taking more attempts from here for now. Try again in a few minutes.'**
  String get pairTooManyAttempts;

  /// No description provided for @pairUnreachable.
  ///
  /// In en, this message translates to:
  /// **'The server stopped answering.'**
  String get pairUnreachable;

  /// No description provided for @pairUnsupported.
  ///
  /// In en, this message translates to:
  /// **'This server is too old to pair with.'**
  String get pairUnsupported;

  /// No description provided for @pairDeviceNameLabel.
  ///
  /// In en, this message translates to:
  /// **'Device name'**
  String get pairDeviceNameLabel;

  /// No description provided for @denialUnauthenticated.
  ///
  /// In en, this message translates to:
  /// **'This device is not paired with that server.'**
  String get denialUnauthenticated;

  /// No description provided for @denialSignatureExpired.
  ///
  /// In en, this message translates to:
  /// **'This phone\'s clock is off, so the server rejected the request. Fix the date and time, then try again.'**
  String get denialSignatureExpired;

  /// No description provided for @denialDeviceRevoked.
  ///
  /// In en, this message translates to:
  /// **'This device was unpaired from that server. Pair it again.'**
  String get denialDeviceRevoked;

  /// No description provided for @denialRunsOnUserMachine.
  ///
  /// In en, this message translates to:
  /// **'That happens on the machine holding the files, so it cannot run from here.'**
  String get denialRunsOnUserMachine;

  /// No description provided for @cancel.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get cancel;

  /// No description provided for @save.
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get save;

  /// No description provided for @retry.
  ///
  /// In en, this message translates to:
  /// **'Try again'**
  String get retry;

  /// No description provided for @devicesTooltip.
  ///
  /// In en, this message translates to:
  /// **'Devices'**
  String get devicesTooltip;

  /// No description provided for @devicesTitle.
  ///
  /// In en, this message translates to:
  /// **'Devices'**
  String get devicesTitle;

  /// No description provided for @devicesWaiting.
  ///
  /// In en, this message translates to:
  /// **'Waiting to be let in'**
  String get devicesWaiting;

  /// No description provided for @devicesPaired.
  ///
  /// In en, this message translates to:
  /// **'Devices with access'**
  String get devicesPaired;

  /// No description provided for @devicesNone.
  ///
  /// In en, this message translates to:
  /// **'No devices are paired with this server.'**
  String get devicesNone;

  /// No description provided for @devicesUnknownAddress.
  ///
  /// In en, this message translates to:
  /// **'Address unknown'**
  String get devicesUnknownAddress;

  /// No description provided for @devicesApprove.
  ///
  /// In en, this message translates to:
  /// **'Approve'**
  String get devicesApprove;

  /// No description provided for @devicesReject.
  ///
  /// In en, this message translates to:
  /// **'Reject'**
  String get devicesReject;

  /// No description provided for @devicesThisDevice.
  ///
  /// In en, this message translates to:
  /// **'This device'**
  String get devicesThisDevice;

  /// No description provided for @devicesNeverSeen.
  ///
  /// In en, this message translates to:
  /// **'Has not connected yet'**
  String get devicesNeverSeen;

  /// No description provided for @devicesSeenJustNow.
  ///
  /// In en, this message translates to:
  /// **'Seen just now'**
  String get devicesSeenJustNow;

  /// No description provided for @devicesSeenMinutesAgo.
  ///
  /// In en, this message translates to:
  /// **'Seen {minutes} min ago'**
  String devicesSeenMinutesAgo(int minutes);

  /// No description provided for @devicesSeenHoursAgo.
  ///
  /// In en, this message translates to:
  /// **'Seen {hours} h ago'**
  String devicesSeenHoursAgo(int hours);

  /// No description provided for @devicesSeenDaysAgo.
  ///
  /// In en, this message translates to:
  /// **'Seen {days} d ago'**
  String devicesSeenDaysAgo(int days);

  /// No description provided for @deviceRenameTitle.
  ///
  /// In en, this message translates to:
  /// **'Rename'**
  String get deviceRenameTitle;

  /// No description provided for @deviceRevoke.
  ///
  /// In en, this message translates to:
  /// **'Remove access'**
  String get deviceRevoke;

  /// No description provided for @deviceRevokeTitle.
  ///
  /// In en, this message translates to:
  /// **'Remove {name}?'**
  String deviceRevokeTitle(String name);

  /// No description provided for @deviceRevokeBody.
  ///
  /// In en, this message translates to:
  /// **'That device will have to pair again before it can reach this server.'**
  String get deviceRevokeBody;

  /// No description provided for @deviceRevokeSelfBody.
  ///
  /// In en, this message translates to:
  /// **'This is the device you are using. Removing its access disconnects you from this server, and you will have to pair again to come back.'**
  String get deviceRevokeSelfBody;

  /// No description provided for @pairVoluntaryIntro.
  ///
  /// In en, this message translates to:
  /// **'This server serves this device already. Pairing names it, lets you manage who else has access, and keeps working if the server is later closed to unknown devices.'**
  String get pairVoluntaryIntro;

  /// No description provided for @pairCancel.
  ///
  /// In en, this message translates to:
  /// **'Not now'**
  String get pairCancel;

  /// No description provided for @notPairedTitle.
  ///
  /// In en, this message translates to:
  /// **'This device is not paired'**
  String get notPairedTitle;

  /// No description provided for @notPairedBody.
  ///
  /// In en, this message translates to:
  /// **'Pair it to manage devices and keep access if this server stops serving unknown ones.'**
  String get notPairedBody;

  /// No description provided for @notPairedAction.
  ///
  /// In en, this message translates to:
  /// **'Pair'**
  String get notPairedAction;

  /// No description provided for @forgetServer.
  ///
  /// In en, this message translates to:
  /// **'Forget'**
  String get forgetServer;

  /// No description provided for @forgetServerTitle.
  ///
  /// In en, this message translates to:
  /// **'Forget {name}?'**
  String forgetServerTitle(String name);

  /// No description provided for @forgetServerBody.
  ///
  /// In en, this message translates to:
  /// **'This removes the server from the list. If this device is paired with it, its key is deleted too and pairing again means a new code or approval.'**
  String get forgetServerBody;

  /// No description provided for @forgetServerConfirm.
  ///
  /// In en, this message translates to:
  /// **'Forget'**
  String get forgetServerConfirm;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'zh'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'zh':
      return AppLocalizationsZh();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
