import 'package:bakabase_mobile/core/media_types.dart';
import 'package:bakabase_mobile/playback/external_players.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('iOS player schemes (ported from playerSchemes.ts)', () {
    const url = 'http://192.168.1.5:34567/file/raw?fullname=%2Fa%2Fb.mkv';

    test('VLC uses the x-callback stream API', () {
      expect(
        ExternalPlayer.iosSchemeUrl('vlc', url),
        'vlc-x-callback://x-callback-url/stream?url=${Uri.encodeComponent(url)}',
      );
    });

    test('Infuse uses the x-callback play API', () {
      expect(
        ExternalPlayer.iosSchemeUrl('infuse', url),
        'infuse://x-callback-url/play?url=${Uri.encodeComponent(url)}',
      );
    });

    test('nPlayer rewrites the scheme instead of taking a parameter', () {
      expect(
        ExternalPlayer.iosSchemeUrl('nplayer', url),
        'nplayer-http://192.168.1.5:34567/file/raw?fullname=%2Fa%2Fb.mkv',
      );
    });

    test('SenPlayer uses its x-callback play API', () {
      expect(
        ExternalPlayer.iosSchemeUrl('senplayer', url),
        'SenPlayer://x-callback-url/play?url=${Uri.encodeComponent(url)}',
      );
    });
  });

  group('media classification', () {
    test('routes by extension, case-insensitively', () {
      expect(classifyPath('/a/Movie.MKV'), MediaKind.video);
      expect(classifyPath('/a/track.flac'), MediaKind.audio);
      expect(classifyPath('/a/page01.WebP'), MediaKind.image);
      expect(classifyPath('/a/readme.txt'), MediaKind.other);
      expect(classifyPath('/a/no-extension'), MediaKind.other);
    });

    test('archive entries are detected by the ! separator', () {
      expect(isArchiveEntry('/a/comic.zip!001.jpg'), isTrue);
      expect(isArchiveEntry('/a/001.jpg'), isFalse);
    });

    test('compressed files are detected by extension', () {
      expect(isCompressedFile('/a/comic.ZIP'), isTrue);
      expect(isCompressedFile('/a/comic.cbz'), isFalse);
      expect(isCompressedFile('/a/movie.mkv'), isFalse);
    });
  });

  group('natural ordering', () {
    test('numbers compare numerically inside names', () {
      final pages = ['p10.jpg', 'p2.jpg', 'p1.jpg']..sort(naturalCompare);
      expect(pages, ['p1.jpg', 'p2.jpg', 'p10.jpg']);
    });

    test('mixed segments and case are handled', () {
      final files = ['B12', 'a2', 'A10', 'a1']..sort(naturalCompare);
      expect(files, ['a1', 'a2', 'A10', 'B12']);
    });
  });
}
