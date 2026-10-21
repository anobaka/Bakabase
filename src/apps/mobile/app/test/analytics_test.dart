import 'dart:async';
import 'dart:convert';

import 'package:bakabase_mobile/core/analytics.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

const deploymentA = '10000000-0000-4000-8000-000000000001';
const deploymentB = '10000000-0000-4000-8000-000000000002';

AnalyticsConfig config({
  String id = deploymentA,
  bool enabled = true,
  Map<String, dynamic> overrides = const {},
}) => AnalyticsConfig.fromJson({
  'enableAnonymousDataTracking': enabled,
  'schemaVersion': 2,
  'identityVersion': 2,
  'deploymentId': id,
  'deploymentType': 'docker_server',
  'appVersion': '2.4.0',
  'serverOs': 'linux',
  'serverArch': 'arm64',
  'environment': 'production',
  'releaseChannel': 'stable',
  'postHogApiKey': 'phc_public_project_key',
  'postHogApiHost': 'https://eu.i.posthog.com',
  ...overrides,
});

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  late MobileAnalytics analytics;
  late List<Map<String, dynamic>> batches;
  late List<Uri> endpoints;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    batches = [];
    endpoints = [];
    analytics = MobileAnalytics(
      version: () async => '0.1.0+1',
      send: (endpoint, payload, _) async {
        endpoints.add(endpoint);
        batches.add(payload);
      },
    );
    await analytics.initialize();
  });
  tearDown(() => analytics.dispose());

  test('only production and staging allow anonymous delivery', () {
    for (final environment in ['production', 'staging']) {
      expect(config(overrides: {'environment': environment}).usable, isTrue);
    }
    for (final environment in [
      'development',
      'test',
      'unknown',
      'custom',
      null,
    ]) {
      expect(config(overrides: {'environment': environment}).usable, isFalse);
    }
  });

  List<Map<String, dynamic>> events() =>
      batches.expand((b) => b['batch'] as List<Map<String, dynamic>>).toList();

  test(
    'shared event names and fixed feature IDs match the web contract',
    () async {
      final binding = analytics.configure(config(), 'http://nas:34567', 1)!;
      for (final event in [
        'resource_query_presented',
        'resource_opened',
        'playback_started',
        'playback_handoff',
        'reader_opened',
      ]) {
        binding.capture(event, {
          'feature_id': '/private/title',
          'search_kind': 'manual',
          'load_mode': 'replace',
          'playback_mode': 'external_player',
          'outcome': 'success',
        });
      }
      binding.capture('client_session_started');
      binding.capture('external_player_handoff');
      binding.capture('playback_handoff', {
        'playback_mode': 'clipboard',
        'outcome': 'success',
      });
      await analytics.flush();
      expect(
        events().where((e) => e['event'] == 'ui_session_started'),
        hasLength(1),
      );
      expect(
        events().where(
          (e) =>
              e['event'] == 'client_session_started' ||
              e['event'] == 'external_player_handoff',
        ),
        isEmpty,
      );
      for (final entry in const {
        'resource_query_presented': 'search',
        'resource_opened': 'resources',
        'playback_started': 'playback',
        'playback_handoff': 'playback',
        'reader_opened': 'reader',
      }.entries) {
        final matching = events().where((e) => e['event'] == entry.key);
        expect(
          matching.every(
            (e) => (e['properties'] as Map)['feature_id'] == entry.value,
          ),
          isTrue,
        );
      }
      final query =
          events().singleWhere(
                (e) => e['event'] == 'resource_query_presented',
              )['properties']
              as Map;
      expect(query['search_kind'], 'manual');
      expect(query['load_mode'], 'replace');
      expect(
        events()
            .where((e) => e['event'] == 'playback_handoff')
            .map((e) => (e['properties'] as Map)['playback_mode']),
        ['external_player', 'clipboard'],
      );
      expect(jsonEncode(batches), isNot(contains('/private/title')));
    },
  );

  test(
    'one random client identity survives server changes and app recreation',
    () async {
      final id = analytics.clientInstallId;
      expect(
        id,
        matches(
          RegExp(
            r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
          ),
        ),
      );
      expect(id, isNot(anyOf(deploymentA, deploymentB)));
      analytics.configure(config(), 'http://nas:34567', 1);
      await analytics.flush();
      analytics.configure(config(id: deploymentB), 'http://desktop:34567', 1);
      await analytics.flush();
      expect(events().map((e) => e['distinct_id']).toSet(), {'v2:client:$id'});
      expect(
        events().where((e) => e['event'] == 'client_first_seen'),
        hasLength(1),
      );
      final next = MobileAnalytics(
        version: () async => '0.1.1+2',
        send: (_, _, _) async {},
      );
      addTearDown(next.dispose);
      await next.initialize();
      expect(next.clientInstallId, id);
    },
  );

  test(
    'both consent switches and valid current configuration are required',
    () async {
      expect(
        analytics.configure(config(enabled: false), 'http://nas:34567', 1),
        isNull,
      );
      await analytics.flush();
      expect(batches, isEmpty);
      final binding = analytics.configure(config(), 'http://nas:34567', 1)!;
      binding.capture('resource_opened');
      await analytics.setLocalEnabled(false);
      binding.capture('resource_opened');
      await analytics.flush();
      expect(batches, isEmpty);
      expect(
        binding.requestHeaders(
          Uri.parse('http://nas:34567/resource/search'),
          MobileAnalytics.newId(),
        ),
        isEmpty,
      );
      expect(analytics.configure(config(), 'http://nas:34567', 1), isNull);
      expect(
        (await SharedPreferences.getInstance()).getBool(
          MobileAnalytics.enabledKey,
        ),
        false,
      );
      await analytics.setLocalEnabled(true);
      binding.capture('resource_opened');
      await analytics.flush();
      expect(
        batches,
        isEmpty,
        reason: 'Re-enabling needs a fresh server configuration.',
      );
    },
  );

  test(
    'switch clears queued events and invalidates late old-server callbacks',
    () async {
      final old = analytics.configure(config(), 'http://nas:34567', 1)!;
      old.capture('resource_opened');
      analytics.clearContext();
      final current = analytics.configure(
        config(id: deploymentB),
        'http://desktop:34567',
        1,
      )!;
      old.capture('playback_started', {
        'player': 'built_in',
        'media_kind': 'video',
      });
      current.capture('resource_opened');
      await analytics.flush();
      expect(
        events().every(
          (e) => (e['properties'] as Map)['deployment_id'] == deploymentB,
        ),
        true,
      );
      expect(events().where((e) => e['event'] == 'playback_started'), isEmpty);
      expect(
        old.requestHeaders(
          Uri.parse('http://nas:34567/resource/search'),
          MobileAnalytics.newId(),
        ),
        isEmpty,
      );
    },
  );

  test(
    'request context only reaches its exact server origin, with anonymous IDs',
    () {
      final binding = analytics.configure(config(), 'http://nas:34567', 1)!;
      final operation = MobileAnalytics.newId();
      final headers = binding.requestHeaders(
        Uri.parse('http://nas:34567/resource/search'),
        operation,
      );
      expect(headers, {
        'X-Bakabase-Analytics-Client': analytics.clientInstallId,
        'X-Bakabase-Analytics-Session': analytics.sessionId,
        'X-Bakabase-Analytics-Operation': operation,
        'X-Bakabase-Analytics-Surface': 'mobile_app',
        'X-Bakabase-Analytics-Consent': '1',
      });
      for (final target in [
        'https://nas:34567/resource/search',
        'http://nas:1234/resource/search',
        'http://other/resource/search',
        'https://eu.i.posthog.com/batch/',
      ]) {
        expect(binding.requestHeaders(Uri.parse(target), operation), isEmpty);
      }
    },
  );

  test(
    'payload is allowlisted including enum values and excludes private content',
    () async {
      final binding = analytics.configure(
        config(),
        'http://private-server:34567',
        1,
      )!;
      binding.capture('resource_query_presented', {
        'has_keyword': true,
        'has_library_filter': false,
        'is_next_page': false,
        'result_count_bucket': '2-10',
        'keyword': 'secret search',
        'path': '/private/movie.mp4',
        'Authorization': 'paired-device-key',
        'actor_type': 'system',
        'deployment_id': deploymentB,
      });
      binding.capture('playback_started', {
        'media_kind': 'private filename',
        'player': 'built_in',
      });
      binding.capture('secret-resource-title', {'title': 'private-title'});
      await analytics.flush();
      expect(endpoints.single.toString(), 'https://eu.i.posthog.com/batch/');
      final encoded = jsonEncode(batches);
      for (final forbidden in [
        'secret search',
        '/private/',
        'paired-device-key',
        'private-server',
        'private filename',
        'secret-resource-title',
        deploymentB,
      ]) {
        expect(encoded, isNot(contains(forbidden)));
      }
      final query = events().singleWhere(
        (e) => e['event'] == 'resource_query_presented',
      );
      final props = query['properties'] as Map;
      expect(props['schema_version'], 2);
      expect(props['identity_version'], 2);
      expect(props['actor_type'], 'client');
      expect(props['client_surface'], 'mobile_app');
      expect(props['client_version'], '0.1.0+1');
      expect(props[r'$process_person_profile'], false);
      expect(props[r'$geoip_disable'], true);
      expect(props['has_keyword'], true);
      expect(props['result_count_bucket'], '2-10');
      expect(query['timestamp'], isNotNull);
      expect(query['uuid'], isNotNull);
    },
  );

  test('old schema, malformed identity and unsafe endpoints fail closed', () {
    for (final overrides in [
      {'schemaVersion': 1},
      {'identityVersion': 1},
      {'deploymentId': 'server-name'},
      {'deploymentType': 'custom device name'},
      {'postHogApiHost': 'http://posthog.local'},
      {'postHogApiHost': 'https://user:password@posthog.example'},
      {'postHogApiHost': 'https://posthog.example/?token=secret'},
    ]) {
      expect(
        analytics.configure(
          config(overrides: overrides),
          'http://nas:34567',
          1,
        ),
        isNull,
      );
    }
  });

  test(
    'buffer is bounded and first observation is not an install event',
    () async {
      final binding = analytics.configure(config(), 'http://nas:34567', 1)!;
      for (var i = 0; i < 1000; i++) {
        binding.capture('resource_opened');
      }
      await analytics.flush();
      expect(events(), hasLength(MobileAnalytics.maxQueueSize));
      expect(events().map((e) => e['event']), contains('client_first_seen'));
      expect(
        events()
            .map((e) => e['event'])
            .any((e) => (e as String).contains('install')),
        false,
      );
      expect(
        events().map((e) => e['uuid']).toSet(),
        hasLength(MobileAnalytics.maxQueueSize),
      );
    },
  );

  test('opt-out cancels an in-flight send and does not retry it', () async {
    final completed = Completer<void>();
    CancelToken? sentToken;
    var sends = 0;
    final pending = MobileAnalytics(
      version: () async => '0.1.0+1',
      send: (_, _, token) {
        sentToken = token;
        sends++;
        return completed.future;
      },
    );
    addTearDown(pending.dispose);
    await pending.initialize();
    pending.configure(config(), 'http://nas:34567', 1);
    final sending = pending.flush();
    await pending.setLocalEnabled(false);
    expect(sentToken!.isCancelled, true);
    completed.completeError(Exception('not persisted or retried'));
    await sending;
    await pending.flush();
    expect(sends, 1);
  });

  test(
    'periodic consent refresh preserves the session; resume starts a new one',
    () async {
      final first = analytics.configure(config(), 'http://nas:34567', 1)!;
      final session = analytics.sessionId;
      first.capture('ui_ready');
      first.capture('ui_ready');
      await analytics.flush();
      analytics.clearContext(keepSession: true);
      final refreshed = analytics.configure(
        config(),
        'http://nas:34567',
        1,
        newSession: false,
      )!;
      refreshed.capture('ui_ready');
      expect(analytics.sessionId, session);
      await analytics.flush();
      expect(events().where((e) => e['event'] == 'ui_ready'), hasLength(1));
      analytics.clearContext();
      analytics.configure(config(), 'http://nas:34567', 1);
      expect(analytics.sessionId, isNot(session));
    },
  );
}
