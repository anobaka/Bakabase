import 'dart:async';
import 'dart:io';
import 'dart:math';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Public, anonymous analytics configuration. Never contains pairing credentials.
class AnalyticsConfig {
  AnalyticsConfig.fromJson(Map<String, dynamic> json)
    : enabled = json['enableAnonymousDataTracking'] == true,
      schemaVersion = json['schemaVersion'],
      identityVersion = json['identityVersion'],
      deploymentId = _uuid(json['deploymentId']),
      deploymentType = _enum(json['deploymentType'], const {
        'desktop_all_in_one',
        'standalone_server',
        'docker_server',
      }),
      serverVersion = _version(json['appVersion']),
      serverOs = _enum(json['serverOs'], const {'windows', 'linux', 'macos'}),
      serverArch = _enum(json['serverArch'], const {
        'x64',
        'x86',
        'arm64',
        'arm',
      }),
      environment = _enum(json['environment'], const {
        'production',
        'development',
        'staging',
        'test',
      }),
      releaseChannel = _enum(json['releaseChannel'], const {
        'stable',
        'beta',
        'dev',
      }),
      apiKey = json['postHogApiKey'] is String
          ? json['postHogApiKey'] as String
          : '',
      host = _host(json['postHogApiHost']);

  final bool enabled;
  final Object? schemaVersion;
  final Object? identityVersion;
  final String? deploymentId;
  final String deploymentType,
      serverVersion,
      serverOs,
      serverArch,
      environment,
      releaseChannel;
  final String apiKey;
  final Uri? host;

  bool get usable =>
      enabled &&
      schemaVersion == 2 &&
      identityVersion == 2 &&
      deploymentId != null &&
      deploymentType != 'unknown' &&
      const {'production', 'staging'}.contains(environment) &&
      apiKey.isNotEmpty &&
      apiKey.length <= 300 &&
      host != null;

  static String? _uuid(Object? value) =>
      value is String &&
          RegExp(
            r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
          ).hasMatch(value)
      ? value.toLowerCase()
      : null;
  static String _enum(Object? value, Set<String> values) =>
      value is String && values.contains(value) ? value : 'unknown';
  static String _version(Object? value) =>
      value is String && RegExp(r'^[0-9A-Za-z.+-]{1,80}$').hasMatch(value)
      ? value
      : 'unknown';
  static Uri? _host(Object? value) {
    final uri = value is String ? Uri.tryParse(value) : null;
    // No redirect, embedded credential, query or fragment is ever sent to capture.
    return uri != null &&
            uri.scheme == 'https' &&
            uri.host.isNotEmpty &&
            uri.userInfo.isEmpty &&
            !uri.hasQuery &&
            !uri.hasFragment
        ? uri
        : null;
  }
}

typedef AnalyticsSend =
    Future<void> Function(
      Uri endpoint,
      Map<String, dynamic> payload,
      CancelToken cancelToken,
    );

/// A capability tied to one connection generation. Late completions from a
/// disconnected server cannot attach themselves to the next server's context.
class AnalyticsBinding {
  AnalyticsBinding._(this._owner, this._generation, this._origin);
  final MobileAnalytics _owner;
  final int _generation;
  final Uri _origin;

  void capture(String event, [Map<String, Object> properties = const {}]) =>
      _owner.capture(event, properties, generation: _generation);

  Map<String, String> requestHeaders(Uri target, String operationId) {
    if (!_owner._active(_generation) ||
        target.origin != _origin.origin ||
        !RegExp(r'^[0-9a-f-]{36}$').hasMatch(operationId)) {
      return const {};
    }
    return {
      'X-Bakabase-Analytics-Client': _owner.clientInstallId!,
      'X-Bakabase-Analytics-Session': _owner.sessionId!,
      'X-Bakabase-Analytics-Operation': operationId,
      'X-Bakabase-Analytics-Surface': 'mobile_app',
      'X-Bakabase-Analytics-Consent': '1',
    };
  }
}

/// Best-effort, bounded, memory-only capture. It never signs requests, records a
/// URL, reads library contents, or makes product actions wait for ingestion.
class MobileAnalytics extends ChangeNotifier {
  MobileAnalytics({AnalyticsSend? send, Future<String> Function()? version})
    : _send = send ?? _sendBatch,
      _version = version ?? _readVersion;

  static const identityKey = 'bakabase.analytics.clientInstallId.v2';
  static const enabledKey = 'bakabase.analytics.enabled';
  static const firstSeenKey = 'bakabase.analytics.firstSeen.v2';
  static const maxQueueSize = 64;
  static const _flushInterval = Duration(seconds: 10);
  static const _maxContextAge = Duration(minutes: 5);
  final AnalyticsSend _send;
  final Future<String> Function() _version;
  final List<Map<String, dynamic>> _pending = [];
  SharedPreferences? _prefs;
  Future<void>? _initializing;
  AnalyticsConfig? _config;
  DateTime? _configuredAt;
  Timer? _timer;
  CancelToken? _inFlight;
  bool _disposed = false;
  int _generation = 0;
  int _protocolVersion = 0;
  String _clientVersion = 'unknown';
  String? clientInstallId;
  String? sessionId;
  String? _reportedUiSession;
  bool localEnabled = true;

  Future<void> initialize() => _initializing ??= _initialize();

  Future<void> _initialize() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      if (_disposed) return;
      _prefs = prefs;
      localEnabled = prefs.getBool(enabledKey) ?? true;
      clientInstallId =
          AnalyticsConfig._uuid(prefs.getString(identityKey)) ?? newId();
      await prefs.setString(identityKey, clientInstallId!);
      _clientVersion = AnalyticsConfig._version(await _version());
      if (!_disposed) notifyListeners();
    } on Object {
      // Storage/platform failures disable telemetry, never app startup.
      clientInstallId = null;
    }
  }

  static Future<String> _readVersion() async {
    final info = await PackageInfo.fromPlatform();
    return '${info.version}+${info.buildNumber}';
  }

  static String newId() {
    final random = Random.secure();
    final bytes = List<int>.generate(16, (_) => random.nextInt(256));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
  }

  Future<void> setLocalEnabled(bool enabled) async {
    await initialize();
    localEnabled = enabled;
    clearContext();
    if (!_disposed) notifyListeners();
    try {
      await _prefs?.setBool(enabledKey, enabled);
    } on Object {
      /* Best effort. */
    }
  }

  /// Also called before refreshing consent. A failed refresh fails closed.
  void clearContext({bool keepSession = false}) {
    _generation++;
    _config = null;
    _configuredAt = null;
    if (!keepSession) sessionId = null;
    _pending.clear();
    _timer?.cancel();
    _timer = null;
    _inFlight?.cancel();
    _inFlight = null;
  }

  AnalyticsBinding? configure(
    AnalyticsConfig config,
    String baseUrl,
    int protocolVersion, {
    bool newSession = true,
  }) {
    final previousSession = sessionId;
    clearContext();
    if (_disposed ||
        !localEnabled ||
        clientInstallId == null ||
        !config.usable) {
      return null;
    }
    _config = config;
    _configuredAt = DateTime.now();
    _protocolVersion = protocolVersion;
    sessionId = newSession ? newId() : (previousSession ?? newId());
    final binding = AnalyticsBinding._(this, _generation, Uri.parse(baseUrl));
    if (_prefs?.getBool(firstSeenKey) != true) {
      binding.capture('client_first_seen');
      // First observation, not first install. Persist only after a successful
      // send, since switching/disconnecting may discard this in-memory batch.
    }
    if (newSession || previousSession == null) {
      binding.capture('ui_session_started');
    }
    notifyListeners();
    return binding;
  }

  bool _active(int generation) =>
      !_disposed &&
      generation == _generation &&
      localEnabled &&
      _config != null &&
      clientInstallId != null &&
      _configuredAt != null &&
      DateTime.now().difference(_configuredAt!) < _maxContextAge;

  // Values as well as keys are bounded: callers cannot smuggle a filename into
  // a generic string property. Counts are coarse, never resource identifiers.
  static const _enums = <String, Set<String>>{
    'outcome': {
      'success',
      'failed',
      'cancelled',
      'rejected',
      'unreachable',
      'unsupported',
    },
    'method': {'code', 'approval'},
    'media_kind': {'video', 'audio', 'image', 'other'},
    'result_count_bucket': {'0', '1', '2-10', '11-50', '51+'},
    'page_count_bucket': {'0', '1', '2-10', '11-50', '51+'},
    'player': {'built_in', 'external'},
    'playback_mode': {'browser', 'native', 'external_player', 'clipboard'},
    'search_kind': {'manual', 'restored', 'refresh'},
    'load_mode': {'append', 'replace', 'prepend'},
    'action': {'open_web_ui', 'view_history', 'view_devices'},
  };
  static const _events = <String, Set<String>>{
    'client_first_seen': {},
    'ui_session_started': {},
    'ui_ready': {},
    'server_connection_result': {'outcome'},
    'pairing_result': {'outcome', 'method'},
    'resource_query_presented': {
      'has_keyword',
      'has_filters',
      'search_kind',
      'load_mode',
      'outcome',
      'has_library_filter',
      'is_next_page',
      'result_count_bucket',
    },
    'resource_opened': {},
    'playback_started': {'media_kind', 'player', 'playback_mode'},
    'reader_opened': {'page_count_bucket', 'media_kind'},
    'playback_handoff': {'outcome', 'media_kind', 'player', 'playback_mode'},
    'client_action_completed': {'action'},
  };
  static const _eventFeatures = <String, String>{
    'resource_query_presented': 'search',
    'resource_opened': 'resources',
    'playback_started': 'playback',
    'playback_handoff': 'playback',
    'reader_opened': 'reader',
  };

  static String countBucket(int count) => count <= 0
      ? '0'
      : count == 1
      ? '1'
      : count <= 10
      ? '2-10'
      : count <= 50
      ? '11-50'
      : '51+';

  void capture(
    String event,
    Map<String, Object> properties, {
    required int generation,
  }) {
    if (!_active(generation) || !_events.containsKey(event)) return;
    if (event == 'ui_ready') {
      if (_reportedUiSession == sessionId) return;
      _reportedUiSession = sessionId;
    }
    final safe = <String, Object>{};
    for (final entry in properties.entries) {
      if (!_events[event]!.contains(entry.key)) continue;
      if ((_enums[entry.key]?.contains(entry.value) ?? false) ||
          ({
                'has_keyword',
                'has_filters',
                'has_library_filter',
                'is_next_page',
              }.contains(entry.key) &&
              entry.value is bool)) {
        safe[entry.key] = entry.value;
      }
    }
    // Fixed by the event contract, never derived from a caller or resource value.
    if (_eventFeatures[event] case final feature?) safe['feature_id'] = feature;
    final config = _config!;
    if (_pending.length >= maxQueueSize) return;
    _pending.add({
      'event': event,
      'distinct_id': 'v2:client:$clientInstallId',
      'timestamp': DateTime.now().toUtc().toIso8601String(),
      'uuid': newId(),
      'properties': {
        ...safe,
        r'$process_person_profile': false,
        r'$geoip_disable': true,
        'schema_version': 2,
        'identity_version': 2,
        'actor_type': 'client',
        'client_surface': 'mobile_app',
        'client_install_id': clientInstallId,
        'session_id': sessionId,
        'deployment_id': config.deploymentId,
        'deployment_type': config.deploymentType,
        'server_version': config.serverVersion,
        'server_os': config.serverOs,
        'server_arch': config.serverArch,
        'environment': config.environment,
        'release_channel': config.releaseChannel,
        'client_version': _clientVersion,
        'client_os': Platform.operatingSystem,
        'protocol_version': _protocolVersion,
      },
    });
    _timer ??= Timer(_flushInterval, () {
      _timer = null;
      unawaited(flush());
    });
  }

  Future<void> flush() async {
    _timer?.cancel();
    _timer = null;
    if (!_active(_generation) || _pending.isEmpty || _inFlight != null) return;
    final config = _config!;
    final batch = List<Map<String, dynamic>>.of(_pending);
    _pending.clear();
    final cancel = CancelToken();
    _inFlight = cancel;
    try {
      final path =
          '${config.host!.path.replaceFirst(RegExp(r'/$'), '')}/batch/';
      await _send(config.host!.replace(path: path), {
        'api_key': config.apiKey,
        'batch': batch,
      }, cancel);
      if (!cancel.isCancelled &&
          batch.any((e) => e['event'] == 'client_first_seen')) {
        await _prefs?.setBool(firstSeenKey, true);
      }
    } on Object {
      // Drop on failure. No unbounded retries, durable queue, or sensitive logging.
    } finally {
      if (identical(_inFlight, cancel)) _inFlight = null;
      if (_pending.isNotEmpty && _active(_generation)) {
        _timer ??= Timer(_flushInterval, () {
          _timer = null;
          unawaited(flush());
        });
      }
    }
  }

  static Future<void> _sendBatch(
    Uri endpoint,
    Map<String, dynamic> payload,
    CancelToken cancel,
  ) async {
    final dio = Dio(
      BaseOptions(
        connectTimeout: const Duration(seconds: 5),
        sendTimeout: const Duration(seconds: 5),
        receiveTimeout: const Duration(seconds: 5),
        followRedirects: false,
      ),
    );
    try {
      await dio.postUri<void>(endpoint, data: payload, cancelToken: cancel);
    } finally {
      dio.close(force: true);
    }
  }

  @override
  void dispose() {
    _disposed = true;
    clearContext();
    super.dispose();
  }
}

final mobileAnalyticsProvider = ChangeNotifierProvider<MobileAnalytics>(
  (ref) => MobileAnalytics(),
);
