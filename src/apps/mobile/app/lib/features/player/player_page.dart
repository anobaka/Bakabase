import 'package:flutter/material.dart';
import 'package:media_kit/media_kit.dart';
import 'package:media_kit_video/media_kit_video.dart';

/// In-app playback via libmpv: the same `/file/raw` bytes a native player
/// would pull, decoded on this device — MKV/HEVC/DTS included, with working
/// seek and zero server CPU. Also handles audio (video area stays black).
class PlayerPage extends StatefulWidget {
  const PlayerPage({super.key, required this.url, required this.title});

  final String url;
  final String title;

  @override
  State<PlayerPage> createState() => _PlayerPageState();
}

class _PlayerPageState extends State<PlayerPage> {
  late final Player _player = Player();
  late final VideoController _controller = VideoController(_player);

  @override
  void initState() {
    super.initState();
    _player.open(Media(widget.url));
  }

  @override
  void dispose() {
    _player.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(widget.title, maxLines: 1, overflow: TextOverflow.ellipsis),
      ),
      body: Center(
        child: Video(controller: _controller),
      ),
    );
  }
}
