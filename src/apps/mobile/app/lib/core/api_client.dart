import 'package:dio/dio.dart';

import 'credentials.dart';
import 'list_string.dart';
import 'models.dart';
import 'signed_media_url.dart';
import 'signing_interceptor.dart';

/// Why a request was turned away, from the `X-Bakabase-Remote-Access` header
/// the server stamps on denials. Mirrors RemoteAccessDenialReason on the C#
/// side; [unknown] covers values a newer server may add.
///
/// The last four arrived with device pairing and each sends the user somewhere
/// different, which is the whole reason they are separate values rather than one
/// "refused": [unauthenticated] means pair this device, [deviceRevoked] means pair it
/// again, [signatureExpired] means fix this phone's clock — telling someone their
/// pairing is broken when their clock is off sends them to the wrong fix — and
/// [runsOnUserMachine] is not a permission at all, but a statement that the action
/// only means something on the machine holding the files.
enum RemoteAccessDenial {
  disabled,
  hostOnly,
  pathNotServable,
  transcodeDisabled,
  runsOnUserMachine,
  unauthenticated,
  signatureExpired,
  deviceRevoked,
  unknown,
}

class ApiException implements Exception {
  ApiException(this.message, {this.denial});

  final String message;
  final RemoteAccessDenial? denial;

  @override
  String toString() => message;
}

/// Thin typed client over the handful of `[RemoteAccessible]` endpoints the
/// app uses. One instance per connected server; the base URL is the address
/// discovery (or the user) produced.
class BakabaseApiClient {
  BakabaseApiClient(
    this.baseUrl, {
    DeviceCredentials? credentials,
    Duration clockOffset = Duration.zero,
    void Function(RemoteAccessDenial denial)? onDenied,
  })  : _credentials = credentials,
        _clockOffset = clockOffset,
        _onDenied = onDenied,
        _dio = Dio(BaseOptions(
          baseUrl: baseUrl,
          connectTimeout: const Duration(seconds: 5),
          receiveTimeout: const Duration(seconds: 30),
          // Denials are handled below rather than thrown as raw DioExceptions.
          validateStatus: (_) => true,
        )) {
    // Unpaired is a normal state: a server with RequirePairing off serves an
    // anonymous device everything it is allowed to. So the interceptor is installed
    // only when there is something to sign with, rather than signing with a blank
    // key and having every request refused.
    if (credentials != null) {
      _dio.interceptors.add(DeviceSigningInterceptor(
        credentials: () => credentials,
        clockOffset: () => clockOffset,
      ));
    }
  }

  final String baseUrl;
  final Dio _dio;

  /// Null when this device has not paired with this server. Exposed so the UI can
  /// tell "not paired" from "paired and refused".
  final DeviceCredentials? _credentials;

  /// How far this device's clock sits from the server's, from the handshake. Held
  /// rather than only closed over by the interceptor, because media-URL tokens carry
  /// an expiry the server checks against *its* clock: minting them off this phone's
  /// clock would hand a skewed device links that are already expired on arrival.
  final Duration _clockOffset;

  /// Told about the refusals that mean this device's credentials stopped working, so
  /// something above can do the one thing a page cannot: change where the app is.
  /// Every page would otherwise have to recognise those two denials and agree on what
  /// to do about them, and none of them did.
  final void Function(RemoteAccessDenial denial)? _onDenied;

  bool get isPaired => _credentials != null;

  /// This URL with a media token appended, or unchanged when this device has no key.
  ///
  /// Covers, raw streams and the extracting endpoint all leave this app: they go into
  /// an `Image.network`, or to VLC/Infuse, and none of those can produce an
  /// `Authorization` header. On a server that requires pairing, an unsigned one is a
  /// 401 — a broken cover, or a player that opens on nothing.
  String _signedUrl(Uri uri) {
    final credentials = _credentials;

    if (credentials == null) {
      return uri.toString();
    }

    return SignedMediaUrl.sign(
      uri,
      credentials: credentials,
      now: DateTime.now().toUtc().add(_clockOffset),
    ).toString();
  }

  /// ResourceAdditionalItem.All. Must be a value the server's enum DEFINES:
  /// hand-built bit combinations (e.g. DisplayName|MediaLibraryName|Cover)
  /// fail model binding, because the composite members share the Properties
  /// bit and the flags decomposition cannot reassemble them. All is also what
  /// the web sends for every resource list, so it is the battle-tested path.
  static const int _gridAdditionalItems = 52064;

  /// ResourceAdditionalItem.Properties — property values, for reading ratings.
  static const int _propertiesAdditionalItem = 32;

  /// PropertyPool.Reserved / ReservedProperty.Rating on the server.
  static const int _reservedRatingPropertyId = 13;

  Future<ServerInfo> serverInfo() async {
    final data = await _get('/remote-access/server-info');
    return ServerInfo.fromJson(_dataOf(data));
  }

  Future<List<MediaLibrary>> mediaLibraries() async {
    final data = await _get('/media-library-v2');
    return (_listOf(data)).map(MediaLibrary.fromJson).toList();
  }

  /// Builds the POST /resource/search body. Static and pure so the filter and
  /// order DSL — the part that silently returns wrong results when malformed —
  /// is unit-testable.
  static Map<String, dynamic> buildSearchBody({
    String? keyword,
    int? mediaLibraryId,
    int page = 1,
    int pageSize = 60,
    int? sortProperty,
    bool sortAsc = false,
  }) {
    return <String, dynamic>{
      'page': page,
      'pageSize': pageSize,
      if (keyword != null && keyword.trim().isNotEmpty) 'keyword': keyword.trim(),
      if (sortProperty != null)
        'orders': [
          {'property': sortProperty, 'asc': sortAsc},
        ],
      if (mediaLibraryId != null)
        'group': {
          'combinator': 1,
          'disabled': false,
          'filters': [
            {
              // Internal property MediaLibraryV2Multi, operation In. The
              // operand is a StandardValue ListString of library ids.
              'propertyPool': 1,
              'propertyId': 25,
              'operation': 15,
              'dbValue': serializeListString([mediaLibraryId.toString()]),
              'disabled': false,
            },
          ],
        },
    };
  }

  Future<SearchResult> search({
    String? keyword,
    int? mediaLibraryId,
    int page = 1,
    int pageSize = 60,
    int? sortProperty,
    bool sortAsc = false,
  }) async {
    final response = await _request(
      'POST',
      '/resource/search',
      queryParameters: {'additionalItems': _gridAdditionalItems},
      body: buildSearchBody(
        keyword: keyword,
        mediaLibraryId: mediaLibraryId,
        page: page,
        pageSize: pageSize,
        sortProperty: sortProperty,
        sortAsc: sortAsc,
      ),
    );

    final resources = (response['data'] as List<dynamic>? ?? const [])
        .whereType<Map<String, dynamic>>()
        .map(ResourceSummary.fromJson)
        .toList();

    return SearchResult(
      resources: resources,
      totalCount: (response['totalCount'] as num?)?.toInt() ?? resources.length,
      page: (response['pageIndex'] as num?)?.toInt() ?? page,
      pageSize: (response['pageSize'] as num?)?.toInt() ?? pageSize,
    );
  }

  Future<List<PlayableItem>> playableItems(int resourceId) async {
    final data = await _get('/resource/$resourceId/playable-items');
    return (_listOf(data)).map(PlayableItem.fromJson).toList();
  }

  /// Every file under a path (or the file itself), naturally ordered by the
  /// server. The ground truth the detail page classifies — playable-items only
  /// reflects the user's curated profile rules and is often empty.
  Future<List<String>> allFiles(String path) async {
    final response = await _request('GET', '/file/all-files', queryParameters: {'path': path});
    return (response['data'] as List<dynamic>? ?? const []).whereType<String>().toList();
  }

  /// Entries inside an archive, as full `archive.zip!inner/path` keys the
  /// file endpoints accept.
  Future<List<String>> compressedEntries(String archivePath) async {
    final response = await _request('GET', '/file/compressed-file/entries',
        queryParameters: {'compressedFilePath': archivePath});
    return _listOf(response)
        .map((e) => e['path'] as String?)
        .whereType<String>()
        .map((inner) => '$archivePath!$inner')
        .toList();
  }

  /// Play history, newest first, with total count for paging.
  Future<(List<PlayHistoryEntry>, int)> playHistory({int page = 1, int pageSize = 50}) async {
    final response = await _request('GET', '/play-history',
        queryParameters: {'pageIndex': page, 'pageSize': pageSize});
    final entries = _listOf(response).map(PlayHistoryEntry.fromJson).toList();
    return (entries, (response['totalCount'] as num?)?.toInt() ?? entries.length);
  }

  /// Batch-resolves resources by id — how the history page turns ids into
  /// titles and covers.
  Future<List<ResourceSummary>> resourcesByIds(List<int> ids) async {
    if (ids.isEmpty) {
      return const [];
    }
    final response = await _request('GET', '/resource/keys', queryParameters: {
      'ids': ids,
      'additionalItems': _gridAdditionalItems,
    });
    return _listOf(response).map(ResourceSummary.fromJson).toList();
  }

  /// Pulls the reserved Rating value out of a full resource JSON payload.
  /// Static and pure for tests; the pool/property keys mirror the server
  /// (`properties[Reserved=2][Rating=13].values[0].value`).
  static double? ratingFromResourceJson(Map<String, dynamic> json) {
    final properties = json['properties'];
    if (properties is! Map<String, dynamic>) {
      return null;
    }
    final reserved = properties['2'];
    if (reserved is! Map<String, dynamic>) {
      return null;
    }
    final rating = reserved['$_reservedRatingPropertyId'];
    if (rating is! Map<String, dynamic>) {
      return null;
    }
    final values = rating['values'];
    if (values is! List || values.isEmpty) {
      return null;
    }
    final first = values.first;
    if (first is! Map<String, dynamic>) {
      return null;
    }
    final value = first['value'];
    return value is num ? value.toDouble() : null;
  }

  Future<double?> resourceRating(int resourceId) async {
    final response = await _request('GET', '/resource/keys', queryParameters: {
      'ids': [resourceId],
      'additionalItems': _propertiesAdditionalItem,
    });
    final list = _listOf(response);
    return list.isEmpty ? null : ratingFromResourceJson(list.first);
  }

  /// Writes the reserved Rating. The value is the Decimal dbValue's
  /// StandardValue serialization — a plain numeric string.
  Future<void> setRating(int resourceId, double rating) async {
    await _request('PUT', '/resource/$resourceId/property-value', body: {
      'propertyId': _reservedRatingPropertyId,
      'isCustomProperty': false,
      'value': rating.toString(),
    });
  }

  /// Records that playback started on this device; the server writes the play
  /// history it cannot observe itself.
  Future<void> markPlayed(int resourceId, {String? item}) async {
    await _request(
      'POST',
      '/resource/$resourceId/played-at',
      queryParameters: {'item': ?item},
    );
  }

  /// Thumbnail for any servable path (a cover file, or the resource's own
  /// path). Sized variants hit the server's response cache.
  String thumbnailUrl(String path, {int? width}) {
    final query = <String, String>{
      'path': path,
      if (width != null) 'w': width.toString(),
    };
    return _signedUrl(
      Uri.parse(baseUrl).replace(path: '/tool/thumbnail', queryParameters: query),
    );
  }

  /// The raw byte stream with range support — what native players get handed.
  /// Deliberately not archive-aware; use [playFileUrl] for archive entries.
  String rawFileUrl(String path) => _signedUrl(
      Uri.parse(baseUrl).replace(path: '/file/raw', queryParameters: {'fullname': path}));

  /// The browser-oriented delivery endpoint. The one thing the app needs it
  /// for is entries inside archives (`archive.zip!inner/file`), which the
  /// server extracts and streams — without seeking.
  String playFileUrl(String path) => _signedUrl(
      Uri.parse(baseUrl).replace(path: '/file/play', queryParameters: {'fullname': path}));

  /// Best stream URL for a playable item: raw (seekable) for plain files,
  /// the extracting endpoint for archive entries.
  String streamUrl(String path) =>
      path.contains('!') ? playFileUrl(path) : rawFileUrl(path);

  /// Every device this server has let in.
  ///
  /// Readable from any paired device, which is what makes it worth having here: a
  /// headless server has no screen to show this on, and the phone is often the only
  /// place its owner can see who else has access.
  Future<List<RemoteDevice>> devices() async {
    return _listOf(await _get('/remote-access/devices'))
        .map(RemoteDevice.fromJson)
        .whereType<RemoteDevice>()
        .toList();
  }

  /// Devices waiting to be let in.
  Future<List<PendingPairingRequest>> pairingRequests() async {
    return _listOf(await _get('/remote-access/pairing/requests'))
        .map(PendingPairingRequest.fromJson)
        .whereType<PendingPairingRequest>()
        .toList();
  }

  /// Lets a waiting device in. The approval records this device as the approver.
  Future<void> approvePairingRequest(String requestId) =>
      _request('POST', '/remote-access/pairing/requests/$requestId/approve');

  Future<void> rejectPairingRequest(String requestId) =>
      _request('POST', '/remote-access/pairing/requests/$requestId/reject');

  /// Takes a device's access away — including this one's, which is the case that
  /// matters: somebody whose phone was stolen needs to cut it off from whatever
  /// device they still have.
  Future<void> revokeDevice(String deviceId) =>
      _request('DELETE', '/remote-access/devices/$deviceId');

  Future<void> renameDevice(String deviceId, String name) => _request(
        'PUT',
        '/remote-access/devices/$deviceId/name',
        body: {'name': name},
      );

  /// This device's own id, or null when it has not paired. Lets the device list
  /// point out which row is the phone in the user's hand.
  String? get deviceId => _credentials?.deviceId;

  Future<Map<String, dynamic>> _get(String path) => _request('GET', path);

  Future<Map<String, dynamic>> _request(
    String method,
    String path, {
    Map<String, dynamic>? queryParameters,
    Object? body,
  }) async {
    final Response<dynamic> response;
    try {
      response = await _dio.request<dynamic>(
        path,
        data: body,
        queryParameters: queryParameters,
        options: Options(method: method),
      );
    } on DioException catch (e) {
      throw ApiException('Could not reach the server: ${e.message}');
    }

    // 401 as well as 403 since pairing landed: the gate answers 401 when a device
    // is unknown or its signature did not check out, and 403 when a known device is
    // refused this particular thing. Both carry the header, and reading only 403
    // turned an unpaired device's every call into a bare "Server error (401)".
    final denialHeader = response.headers.value('X-Bakabase-Remote-Access');
    if (denialHeader != null &&
        (response.statusCode == 403 || response.statusCode == 401)) {
      final denial = parseDenial(denialHeader);

      // Reported before the throw, so a caller that swallows the exception — the
      // pairing probe does exactly that — still cannot hide a dead key from the
      // connection above it.
      if (denial == RemoteAccessDenial.deviceRevoked ||
          denial == RemoteAccessDenial.signatureExpired) {
        _onDenied?.call(denial);
      }

      throw ApiException(
        _messageOf(response.data) ?? 'The server refused this request.',
        denial: denial,
      );
    }

    final status = response.statusCode ?? 0;
    if (status < 200 || status >= 300) {
      throw ApiException(_messageOf(response.data) ?? 'Server error ($status).');
    }

    final data = response.data;
    if (data is! Map<String, dynamic>) {
      throw ApiException('Unexpected response shape from $path.');
    }

    final code = (data['code'] as num?)?.toInt() ?? 0;
    if (code != 0) {
      throw ApiException(data['message'] as String? ?? 'Server reported error $code.');
    }

    return data;
  }

  static Map<String, dynamic> _dataOf(Map<String, dynamic> envelope) {
    final data = envelope['data'];
    if (data is Map<String, dynamic>) {
      return data;
    }
    throw ApiException('Response carried no data.');
  }

  static List<Map<String, dynamic>> _listOf(Map<String, dynamic> envelope) {
    return (envelope['data'] as List<dynamic>? ?? const [])
        .whereType<Map<String, dynamic>>()
        .toList();
  }

  static String? _messageOf(dynamic data) =>
      data is Map<String, dynamic> ? data['message'] as String? : null;

  /// The header carries the enum's name, not its number — a bare 6 in a network log
  /// tells nobody anything.
  static RemoteAccessDenial parseDenial(String value) => switch (value) {
        'Disabled' => RemoteAccessDenial.disabled,
        'HostOnly' => RemoteAccessDenial.hostOnly,
        'PathNotServable' => RemoteAccessDenial.pathNotServable,
        'TranscodeDisabled' => RemoteAccessDenial.transcodeDisabled,
        'RunsOnUserMachine' => RemoteAccessDenial.runsOnUserMachine,
        'Unauthenticated' => RemoteAccessDenial.unauthenticated,
        'SignatureExpired' => RemoteAccessDenial.signatureExpired,
        'DeviceRevoked' => RemoteAccessDenial.deviceRevoked,
        _ => RemoteAccessDenial.unknown,
      };
}
