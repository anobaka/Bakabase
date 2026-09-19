import 'package:bakabase_mobile/core/credentials.dart';
import 'package:bakabase_mobile/core/request_signature.dart';
import 'package:bakabase_mobile/core/signed_media_url.dart';
import 'package:flutter_test/flutter_test.dart';

/// The Dart half of the URL-token contract with `SignedMediaUrl` on the C# side.
///
/// Same approach as the header-signature vectors: the expected signatures were
/// computed independently (Python's hmac over the same canonical string), so both
/// implementations check themselves against a third opinion rather than against each
/// other's round trip.
void main() {
  // Bytes 0..31, the key the C# tests hardcode.
  final key = List<int>.generate(32, (i) => i);
  const deviceId = 'dev-1';
  const expiry = 1767225600;
  final expiresAt = DateTime.fromMillisecondsSinceEpoch(expiry * 1000, isUtc: true);

  final credentials =
      DeviceCredentials(deviceId: deviceId, key: RemoteRequestSignature.toBase64Url(key));

  group('canonical string', () {
    test('is six lines with no trailing separator', () {
      final value = SignedMediaUrl.buildCanonicalString(
        deviceId: deviceId,
        method: 'GET',
        path: '/tool/thumbnail',
        rawQuery: 'path=%2Fmedia%2Fa.jpg&w=600',
        expiresAtSeconds: expiry,
      );

      expect(
        value,
        'bkb-url-1\ndev-1\nGET\n/tool/thumbnail\npath=%2Fmedia%2Fa.jpg&w=600\n1767225600',
      );
      expect('\n'.allMatches(value).length, 5);
    });

    test('upper-cases the method and leaves path and query verbatim', () {
      String build(String method) => SignedMediaUrl.buildCanonicalString(
            deviceId: deviceId,
            method: method,
            path: '/Tool/Thumbnail',
            rawQuery: 'path=%2FA%2Bb.jpg&w=600',
            expiresAtSeconds: expiry,
          );

      expect(build('get'), build('GET'));
      expect(build('get'), contains('/Tool/Thumbnail'));
      expect(build('get'), contains('path=%2FA%2Bb.jpg&w=600'));
    });

    test('is a different format from the header signature, with the same key', () {
      // The two are signed with one key, so the version line is what stops a captured
      // header signature being replayed as a URL token or the reverse.
      final urlCanonical = SignedMediaUrl.buildCanonicalString(
        deviceId: deviceId,
        method: 'GET',
        path: '/tool/thumbnail',
        rawQuery: 'path=%2Fmedia%2Fa.jpg&w=600',
        expiresAtSeconds: expiry,
      );

      expect(RemoteRequestSignature.sign(key, urlCanonical),
          '51fWw1h_LLFVx9jaVsvSXI0vQ9AaQoRfxs5fS4haRQQ');
      expect(
        RemoteRequestSignature.sign(
            key, urlCanonical.replaceFirst('bkb-url-1\n', '${RemoteRequestSignature.version}\n')),
        isNot('51fWw1h_LLFVx9jaVsvSXI0vQ9AaQoRfxs5fS4haRQQ'),
      );
    });
  });

  group('token', () {
    test('matches the thumbnail vector', () {
      final token = SignedMediaUrl.buildToken(
        key: key,
        deviceId: deviceId,
        path: '/tool/thumbnail',
        rawQuery: 'path=%2Fmedia%2Fa.jpg&w=600',
        expiresAt: expiresAt,
      );

      expect(token,
          'bkb-url-1.dev-1.1767225600.51fWw1h_LLFVx9jaVsvSXI0vQ9AaQoRfxs5fS4haRQQ');
    });

    test('matches the raw-file vector', () {
      final token = SignedMediaUrl.buildToken(
        key: key,
        deviceId: deviceId,
        path: '/file/raw',
        rawQuery: 'fullname=%2Fmedia%2Fa.mkv',
        expiresAt: expiresAt,
      );

      expect(token,
          'bkb-url-1.dev-1.1767225600.szVO8bnx9NGGVTpYX-fUOosj4Wvz8vlxUu_B3SJEwZU');
    });

    test('is four dot-separated parts, so the server can split it', () {
      final token = SignedMediaUrl.buildToken(
        key: key,
        deviceId: deviceId,
        path: '/file/raw',
        rawQuery: '',
        expiresAt: expiresAt,
      );

      expect(token.split('.').length, 4);
      expect(token.split('.').first, 'bkb-url-1');
    });
  });

  group('signing a url', () {
    test('appends the token and disturbs nothing already in the query', () {
      final signed = SignedMediaUrl.sign(
        Uri.parse('http://host:34567/tool/thumbnail?path=%2Fmedia%2Fa.jpg&w=600'),
        credentials: credentials,
        now: expiresAt.subtract(SignedMediaUrl.defaultLifetime),
      );

      expect(signed.path, '/tool/thumbnail');
      expect(
        signed.query,
        'path=%2Fmedia%2Fa.jpg&w=600&bkbt=bkb-url-1.dev-1.1767225600'
        '.51fWw1h_LLFVx9jaVsvSXI0vQ9AaQoRfxs5fS4haRQQ',
      );
    });

    test('signs what it sends — the token covers the query as it goes on the wire', () {
      // The server rebuilds the canonical string from the query it received, minus this
      // parameter. Anything that re-encoded the rest between signing and sending would
      // fail there and nowhere else.
      final uri = Uri.parse('http://host/file/raw')
          .replace(queryParameters: {'fullname': '/media/Show [2024]/ep01.mkv'});
      final signed = SignedMediaUrl.sign(uri, credentials: credentials, now: expiresAt);

      final stripped = signed.query.substring(0, signed.query.lastIndexOf('&bkbt='));
      expect(stripped, uri.query);

      final token = signed.queryParameters[SignedMediaUrl.queryKey]!;
      expect(
        token.split('.').last,
        RemoteRequestSignature.sign(
          key,
          SignedMediaUrl.buildCanonicalString(
            deviceId: deviceId,
            method: 'GET',
            path: '/file/raw',
            rawQuery: stripped,
            expiresAtSeconds: int.parse(token.split('.')[2]),
          ),
        ),
      );
    });

    test('adds the only parameter when there is no query', () {
      final signed = SignedMediaUrl.sign(Uri.parse('http://host/file/raw'),
          credentials: credentials, now: expiresAt);

      expect(signed.query.startsWith('bkbt='), isTrue);
      expect(signed.query.contains('&'), isFalse);
    });

    test('clamps a lifetime past what the server will honour', () {
      // Beyond MaxLifetime the server refuses the link, so minting one is handing the
      // user a URL that is dead on arrival.
      final signed = SignedMediaUrl.sign(
        Uri.parse('http://host/file/raw?fullname=%2Fa.mkv'),
        credentials: credentials,
        now: expiresAt,
        lifetime: const Duration(days: 400),
      );

      final token = signed.queryParameters[SignedMediaUrl.queryKey]!;
      final expires = int.parse(token.split('.')[2]);

      expect(expires, expiry + SignedMediaUrl.maxLifetime.inSeconds);
    });

    test('mints against the server clock, not this phone\'s', () {
      // A phone an hour behind would otherwise produce links the server reads as
      // nearly expired, and one an hour ahead links it refuses outright.
      final onServerClock = SignedMediaUrl.sign(
        Uri.parse('http://host/file/raw?fullname=%2Fa.mkv'),
        credentials: credentials,
        now: expiresAt.add(const Duration(hours: 1)),
      );
      final onPhoneClock = SignedMediaUrl.sign(
        Uri.parse('http://host/file/raw?fullname=%2Fa.mkv'),
        credentials: credentials,
        now: expiresAt,
      );

      int expiryOf(Uri uri) =>
          int.parse(uri.queryParameters[SignedMediaUrl.queryKey]!.split('.')[2]);

      expect(expiryOf(onServerClock) - expiryOf(onPhoneClock), const Duration(hours: 1).inSeconds);
    });
  });
}
