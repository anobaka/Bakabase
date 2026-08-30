/// Client-side media classification by extension, used to pick a playback
/// surface (player / reader / none). A rough mirror of the server's
/// InternalOptions extension lists — only what the UI needs to route a tap.
library;

enum MediaKind { video, audio, image, other }

const _videoExtensions = {
  '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.flv', '.webm', '.m4v', '.ts',
  '.m2ts', '.mpg', '.mpeg', '.rm', '.rmvb', '.ogv', '.3gp', '.vob',
};

const _audioExtensions = {
  '.mp3', '.flac', '.aac', '.m4a', '.wav', '.ogg', '.opus', '.wma', '.ape',
  '.alac', '.aiff', '.dsf', '.dff',
};

const _imageExtensions = {
  '.jpg', '.jpeg', '.png', '.gif', '.webp', '.bmp', '.avif', '.heic', '.jxl',
  '.tiff', '.tif',
};

/// The server addresses entries inside archives as `archive.zip!inner/path`.
const archiveEntrySeparator = '!';

/// Mirror of the server's InternalOptions.CompressedFileExtensions.
const _compressedExtensions = {
  '.rar', '.7z', '.zip', '.tar', '.bz2', '.gz', '.tgz',
};

MediaKind classifyPath(String path) {
  final dot = path.lastIndexOf('.');
  if (dot < 0) {
    return MediaKind.other;
  }
  final ext = path.substring(dot).toLowerCase();
  if (_videoExtensions.contains(ext)) {
    return MediaKind.video;
  }
  if (_audioExtensions.contains(ext)) {
    return MediaKind.audio;
  }
  if (_imageExtensions.contains(ext)) {
    return MediaKind.image;
  }
  return MediaKind.other;
}

bool isArchiveEntry(String path) => path.contains(archiveEntrySeparator);

bool isCompressedFile(String path) {
  final dot = path.lastIndexOf('.');
  return dot >= 0 && _compressedExtensions.contains(path.substring(dot).toLowerCase());
}

/// Natural ordering ("page2" before "page10"), so comic pages read in the
/// order humans numbered them. The server's all-files endpoint already sorts
/// naturally; archive entries arrive unsorted and go through this.
int naturalCompare(String a, String b) {
  final digits = RegExp(r'\d+|\D+');
  final aParts = digits.allMatches(a.toLowerCase()).map((m) => m.group(0)!).toList();
  final bParts = digits.allMatches(b.toLowerCase()).map((m) => m.group(0)!).toList();

  for (var i = 0; i < aParts.length && i < bParts.length; i++) {
    final aNum = int.tryParse(aParts[i]);
    final bNum = int.tryParse(bParts[i]);
    int result;
    if (aNum != null && bNum != null) {
      result = aNum.compareTo(bNum);
    } else {
      result = aParts[i].compareTo(bParts[i]);
    }
    if (result != 0) {
      return result;
    }
  }

  return aParts.length.compareTo(bParts.length);
}
