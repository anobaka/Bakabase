// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Chinese (`zh`).
class AppLocalizationsZh extends AppLocalizations {
  AppLocalizationsZh([String locale = 'zh']) : super(locale);

  @override
  String get appTitle => 'Bakabase';

  @override
  String get connectTitle => '连接到 Bakabase';

  @override
  String get connecting => '连接中…';

  @override
  String get onThisNetwork => '本网络中';

  @override
  String get discoveryHint =>
      '搜索中… 请确认 Bakabase 正在运行且已开启远程访问，并且本设备与它在同一网络。iOS 首次使用时请允许「本地网络」权限。';

  @override
  String get remembered => '最近连接';

  @override
  String get rememberedEmpty => '连接过的服务器会记在这里。';

  @override
  String get byAddress => '手动输入地址';

  @override
  String get connect => '连接';

  @override
  String couldNotConnect(String url) {
    return '无法连接到 $url';
  }

  @override
  String get remoteAccessDisabledHint =>
      '远程访问未开启。请在主机上的 Bakabase 中开启（设置 → 远程访问）。';

  @override
  String protocolTooNew(String version) {
    return '服务端协议为 v$version，当前 App 版本过旧，请升级 App。';
  }

  @override
  String protocolTooOld(String version) {
    return '服务端协议为 v$version，需要更新主机上的 Bakabase。';
  }

  @override
  String get searchHint => '搜索资源';

  @override
  String get allLibraries => '全部';

  @override
  String get sortTooltip => '排序';

  @override
  String get sortAddDt => '最近添加';

  @override
  String get sortPlayedAt => '最近播放';

  @override
  String get sortFileModifyDt => '文件修改时间';

  @override
  String get sortFilename => '文件名';

  @override
  String get ascending => '升序 ↑';

  @override
  String get descending => '降序 ↓';

  @override
  String get playHistoryTooltip => '播放历史';

  @override
  String get switchServerTooltip => '切换服务器';

  @override
  String get openWebUiTooltip => '打开完整 Web 界面';

  @override
  String get playHere => '在本机播放';

  @override
  String get builtInPlayer => '内置播放器（mpv）';

  @override
  String get copyStreamLink => '复制流地址';

  @override
  String get streamLinkCopied => '流地址已复制，粘贴到任意播放器即可播放';

  @override
  String couldNotOpenPlayer(String name) {
    return '无法打开 $name，请确认已安装';
  }

  @override
  String get otherPlayer => '其他播放器…';

  @override
  String readPages(int count) {
    return '阅读（共 $count 页）';
  }

  @override
  String get playSection => '播放';

  @override
  String get otherFiles => '其他文件';

  @override
  String get noPlayableFiles => '此资源中没有找到可播放的文件。';

  @override
  String get playHistory => '播放历史';

  @override
  String removedResource(int id) {
    return '资源 #$id（已删除）';
  }

  @override
  String get pairTitle => '配对此设备';

  @override
  String get pairIntro =>
      '该服务端只服务它认识的设备。在服务端的 Bakabase 里读取配对码，或者请已配对设备旁的人批准这一台。';

  @override
  String get pairCodeLabel => '6 位配对码';

  @override
  String get pairSubmit => '配对';

  @override
  String get pairAskInstead => '改为请求批准';

  @override
  String get pairWaiting => '等待有人批准此设备…';

  @override
  String get pairCodeRejected => '配对码错误或已过期，请在服务端重新获取。';

  @override
  String get pairRequestRejected => '请求被拒绝，或等待超时。';

  @override
  String get pairTooManyAttempts => '服务端暂时不再接受来自此处的尝试，请几分钟后再试。';

  @override
  String get pairUnreachable => '服务端没有响应。';

  @override
  String get pairUnsupported => '该服务端版本过旧，不支持配对。';

  @override
  String get pairDeviceNameLabel => '设备名称';

  @override
  String get denialUnauthenticated => '此设备尚未与该服务端配对。';

  @override
  String get denialSignatureExpired => '此手机的时钟不准，服务端拒绝了请求。请校正日期与时间后重试。';

  @override
  String get denialDeviceRevoked => '此设备已被该服务端解除配对，请重新配对。';

  @override
  String get denialRunsOnUserMachine => '该操作发生在存放文件的机器上，无法从这里执行。';

  @override
  String get cancel => '取消';

  @override
  String get save => '保存';

  @override
  String get retry => '重试';

  @override
  String get devicesTooltip => '设备';

  @override
  String get devicesTitle => '设备';

  @override
  String get devicesWaiting => '等待放行';

  @override
  String get devicesPaired => '已有访问权限的设备';

  @override
  String get devicesNone => '还没有设备与该服务端配对。';

  @override
  String get devicesUnknownAddress => '地址未知';

  @override
  String get devicesApprove => '批准';

  @override
  String get devicesReject => '拒绝';

  @override
  String get devicesThisDevice => '本设备';

  @override
  String get devicesNeverSeen => '还没有连接过';

  @override
  String get devicesSeenJustNow => '刚刚在线';

  @override
  String devicesSeenMinutesAgo(int minutes) {
    return '$minutes 分钟前在线';
  }

  @override
  String devicesSeenHoursAgo(int hours) {
    return '$hours 小时前在线';
  }

  @override
  String devicesSeenDaysAgo(int days) {
    return '$days 天前在线';
  }

  @override
  String get deviceRenameTitle => '重命名';

  @override
  String get deviceRevoke => '移除访问权限';

  @override
  String deviceRevokeTitle(String name) {
    return '移除 $name？';
  }

  @override
  String get deviceRevokeBody => '该设备需要重新配对才能再访问此服务端。';

  @override
  String get deviceRevokeSelfBody =>
      '这就是你正在使用的设备。移除它的访问权限会断开与该服务端的连接，需要重新配对才能回来。';

  @override
  String get pairVoluntaryIntro =>
      '该服务端已经在为这台设备服务。配对会给它一个名字，让你管理谁还有访问权限；以后服务端若改为只服务已知设备，也不会断。';

  @override
  String get pairCancel => '暂不配对';

  @override
  String get notPairedTitle => '此设备尚未配对';

  @override
  String get notPairedBody => '配对后可以管理设备；服务端以后若不再服务未知设备，访问也不会中断。';

  @override
  String get notPairedAction => '配对';

  @override
  String get forgetServer => '忘记';

  @override
  String forgetServerTitle(String name) {
    return '忘记 $name？';
  }

  @override
  String get forgetServerBody =>
      '会把该服务器从列表中移除。如果本设备与它配对过，密钥也会一并删除，再次配对需要新的配对码或批准。';

  @override
  String get forgetServerConfirm => '忘记';
}
