import 'package:dio/dio.dart';

import 'list_string.dart';
import 'models.dart';

/// Why a request was turned away, from the `X-Bakabase-Remote-Access` header
/// the server stamps on denials. Mirrors RemoteAccessDenialReason on the C#
/// side; [unknown] covers values a newer server may add.
enum RemoteAccessDenial { disabled, hostOnly, pathNotServable, transcodeDisabled, unknown }

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
  BakabaseApiClient(this.baseUrl)
      : _dio = Dio(BaseOptions(
          baseUrl: baseUrl,
          connectTimeout: const Duration(seconds: 5),
          receiveTimeout: const Duration(seconds: 30),
          // Denials are handled below rather than thrown as raw DioExceptions.
          validateStatus: (_) => true,
        ));

  final String baseUrl;
  final Dio _dio;

  /// DisplayName | MediaLibraryName | Cover, the additional items the resource
  /// grid needs. Values mirror ResourceAdditionalItem on the server.
  static const int _gridAdditionalItems = 288 | 2048 | 16416;

  Future<ServerInfo> serverInfo() async {
    final data = await _get('/remote-access/server-info');
    return ServerInfo.fromJson(_dataOf(data));
  }

  Future<List<MediaLibrary>> mediaLibraries() async {
    final data = await _get('/media-library-v2');
    return (_listOf(data)).map(MediaLibrary.fromJson).toList();
  }

  Future<SearchResult> search({
    String? keyword,
    int? mediaLibraryId,
    int page = 1,
    int pageSize = 60,
  }) async {
    final body = <String, dynamic>{
      'page': page,
      'pageSize': pageSize,
      if (keyword != null && keyword.trim().isNotEmpty) 'keyword': keyword.trim(),
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

    final response = await _request(
      'POST',
      '/resource/search',
      queryParameters: {'additionalItems': _gridAdditionalItems},
      body: body,
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
    return Uri.parse(baseUrl)
        .replace(path: '/tool/thumbnail', queryParameters: query)
        .toString();
  }

  /// The raw byte stream with range support — what native players get handed.
  /// Deliberately not archive-aware; use [playFileUrl] for archive entries.
  String rawFileUrl(String path) => Uri.parse(baseUrl)
      .replace(path: '/file/raw', queryParameters: {'fullname': path}).toString();

  /// The browser-oriented delivery endpoint. The one thing the app needs it
  /// for is entries inside archives (`archive.zip!inner/file`), which the
  /// server extracts and streams — without seeking.
  String playFileUrl(String path) => Uri.parse(baseUrl)
      .replace(path: '/file/play', queryParameters: {'fullname': path}).toString();

  /// Best stream URL for a playable item: raw (seekable) for plain files,
  /// the extracting endpoint for archive entries.
  String streamUrl(String path) =>
      path.contains('!') ? playFileUrl(path) : rawFileUrl(path);

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

    final denialHeader = response.headers.value('X-Bakabase-Remote-Access');
    if (denialHeader != null && response.statusCode == 403) {
      throw ApiException(
        _messageOf(response.data) ?? 'The server refused this request.',
        denial: _parseDenial(denialHeader),
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

  static RemoteAccessDenial _parseDenial(String value) => switch (value) {
        'Disabled' => RemoteAccessDenial.disabled,
        'HostOnly' => RemoteAccessDenial.hostOnly,
        'PathNotServable' => RemoteAccessDenial.pathNotServable,
        'TranscodeDisabled' => RemoteAccessDenial.transcodeDisabled,
        _ => RemoteAccessDenial.unknown,
      };
}
