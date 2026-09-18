import 'credentials.dart';
import 'request_signature.dart';

/// A URL a paired device can hand to something that cannot sign anything: an external
/// player, an `<img>` tag, a subtitle downloader.
///
/// The Dart half of `SignedMediaUrl` on the C# side, and the same kind of contract as
/// [RemoteRequestSignature] — two implementations that never compile together, pinned
/// to the same golden vectors rather than to each other.
///
/// The device signs these itself, with the same key it signs headers with. There is no
/// issuing endpoint and no server-held secret, which is what makes revocation total:
/// the server verifies against the device's stored key, so removing the device kills
/// every link it ever produced, including ones already sitting in a player's playlist.
///
/// The token covers one URL — method, path and the rest of the query — so it cannot be
/// carried to another endpoint or another file. It deliberately has no nonce: video
/// playback issues dozens of range requests against the same URL, and treating the
/// second one as a replay would break playback rather than protect anything.
class SignedMediaUrl {
  SignedMediaUrl._();

  /// Query parameter the token rides in.
  static const String queryKey = 'bkbt';

  /// First line of the canonical string, and the token's own prefix. Deliberately
  /// unlike [RemoteRequestSignature.version]: the two formats are signed with the same
  /// key, and differing here is what stops a captured header signature from being
  /// replayed as a URL token, or the reverse.
  static const String version = 'bkb-url-1';

  static const Duration defaultLifetime = Duration(hours: 12);

  /// The furthest expiry the server will honour. Minting beyond it produces a link
  /// that is refused on arrival, so callers that pick their own lifetime are clamped.
  static const Duration maxLifetime = Duration(days: 7);

  /// The bytes both sides run the HMAC over. The method is part of it and is always
  /// `GET` in practice — these are links to fetch, and pinning it stops a token from
  /// being turned into a write.
  ///
  /// [rawQuery] is the query exactly as it will appear on the wire, minus the token's
  /// own parameter and without the leading `?`. Not re-encoded and not reordered: the
  /// client builds the URL it is about to hand out, so what it signs is what it sends.
  static String buildCanonicalString({
    required String deviceId,
    required String method,
    required String path,
    required String rawQuery,
    required int expiresAtSeconds,
  }) =>
      '$version\n'
      '$deviceId\n'
      '${method.toUpperCase()}\n'
      '$path\n'
      '$rawQuery\n'
      '$expiresAtSeconds';

  /// Builds the token for one URL. [rawQuery] must already be the final query minus
  /// this parameter.
  static String buildToken({
    required List<int> key,
    required String deviceId,
    required String path,
    required String rawQuery,
    required DateTime expiresAt,
  }) {
    final expiry = expiresAt.toUtc().millisecondsSinceEpoch ~/ 1000;
    final canonical = buildCanonicalString(
      deviceId: deviceId,
      method: 'GET',
      path: path,
      rawQuery: rawQuery,
      expiresAtSeconds: expiry,
    );

    return '$version.$deviceId.$expiry.${RemoteRequestSignature.sign(key, canonical)}';
  }

  /// Appends a token to [uri], signing the URL exactly as it already stands.
  ///
  /// The query is read back off [uri] rather than rebuilt from a parameter map, for
  /// the same reason the header signer does it: re-encoding is how two implementations
  /// come to disagree, and `?fullname=` carries a whole filesystem path.
  ///
  /// [now] should already be on the server's clock — pass the handshake offset in, or
  /// a phone whose own clock is off mints links that are expired on arrival.
  static Uri sign(
    Uri uri, {
    required DeviceCredentials credentials,
    required DateTime now,
    Duration lifetime = defaultLifetime,
  }) {
    final rawQuery = uri.query;
    final token = buildToken(
      key: RemoteRequestSignature.fromBase64Url(credentials.key),
      deviceId: credentials.deviceId,
      path: uri.path,
      rawQuery: rawQuery,
      expiresAt: now.toUtc().add(lifetime > maxLifetime ? maxLifetime : lifetime),
    );

    // Appended textually rather than through queryParameters, which would re-encode
    // everything already in there and invalidate the signature just produced.
    return uri.replace(
      query: rawQuery.isEmpty ? '$queryKey=$token' : '$rawQuery&$queryKey=$token',
    );
  }
}
