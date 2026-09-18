import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/connection.dart';
import '../../core/pairing.dart';
import '../../l10n/app_localizations.dart';

/// Getting a key for this device, by either of the two routes the server offers.
///
/// Shown only when a server actually refused this device for want of one — the
/// connect handshake decides that by asking for something real rather than by reading
/// a flag, so a server with pairing switched off never brings anyone here.
class PairPage extends ConsumerStatefulWidget {
  const PairPage({super.key, required this.target});

  final NeedsPairing target;

  @override
  ConsumerState<PairPage> createState() => _PairPageState();
}

class _PairPageState extends ConsumerState<PairPage> {
  late final PairingService _pairing = PairingService(widget.target.baseUrl);
  final TextEditingController _code = TextEditingController();
  late final TextEditingController _deviceName =
      TextEditingController(text: _defaultDeviceName());

  Timer? _polling;
  bool _busy = false;
  String? _error;
  bool _waitingForApproval = false;

  static String _defaultDeviceName() {
    // Something the person approving will recognise. Platform.localHostname is the
    // device name on iOS and Android, and a plain hostname elsewhere.
    try {
      return Platform.localHostname;
    } on Object {
      return 'Bakabase mobile';
    }
  }

  @override
  void dispose() {
    _polling?.cancel();
    _code.dispose();
    _deviceName.dispose();
    super.dispose();
  }

  String _describe(AppLocalizations l10n, PairingOutcome outcome) => switch (outcome) {
        PairingOutcome.codeRejected => l10n.pairCodeRejected,
        PairingOutcome.requestRejected => l10n.pairRequestRejected,
        PairingOutcome.tooManyAttempts => l10n.pairTooManyAttempts,
        PairingOutcome.unreachable => l10n.pairUnreachable,
        PairingOutcome.unsupported => l10n.pairUnsupported,
        // Neither is an error worth wording: paired is success, and awaiting approval
        // has its own line on the page.
        PairingOutcome.paired || PairingOutcome.awaitingApproval => '',
      };

  Future<void> _finish(PairingResult result) async {
    final credentials = result.credentials;

    if (credentials == null) {
      return;
    }

    _polling?.cancel();

    // No navigation here on purpose: this page is what the app shows *because* the
    // connection state says NeedsPairing. Storing the credentials moves that state to
    // Connected, and the app swaps this page for the library on its own. Popping as
    // well would be popping a route nothing pushed.
    await ref.read(connectionProvider.notifier).completePairing(
          widget.target.baseUrl,
          widget.target.server,
          widget.target.clockOffset,
          credentials,
        );
  }

  Future<void> _pairWithCode() async {
    setState(() {
      _busy = true;
      _error = null;
    });

    final result = await _pairing.pairWithCode(_code.text, _deviceName.text.trim());

    if (!mounted) {
      return;
    }

    if (result.outcome == PairingOutcome.paired) {
      await _finish(result);
      return;
    }

    setState(() {
      _busy = false;
      _error = _describe(AppLocalizations.of(context)!, result.outcome);
    });
  }

  Future<void> _requestApproval() async {
    setState(() {
      _busy = true;
      _error = null;
    });

    final result = await _pairing.requestPairing(_deviceName.text.trim());

    if (!mounted) {
      return;
    }

    if (result.outcome != PairingOutcome.awaitingApproval || result.requestId == null) {
      setState(() {
        _busy = false;
        _error = _describe(AppLocalizations.of(context)!, result.outcome);
      });
      return;
    }

    setState(() {
      _busy = false;
      _waitingForApproval = true;
    });

    // Polled rather than pushed: approval happens on another device, and this one has
    // no hub connection — establishing one is what is being asked for.
    final requestId = result.requestId!;
    _polling = Timer.periodic(const Duration(seconds: 2), (_) async {
      final claim = await _pairing.claim(requestId);

      if (!mounted) {
        return;
      }

      if (claim.outcome == PairingOutcome.paired) {
        await _finish(claim);
      } else if (claim.outcome != PairingOutcome.awaitingApproval) {
        _polling?.cancel();
        setState(() {
          _waitingForApproval = false;
          _error = _describe(AppLocalizations.of(context)!, claim.outcome);
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;

    // Nothing pushed this route — the app shows it because of the connection state —
    // so leaving means putting that state back, not popping.
    final leave = ref.read(connectionProvider.notifier).cancelPairing;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.pairTitle),
        leading: IconButton(
          icon: const Icon(Icons.close),
          tooltip: l10n.pairCancel,
          onPressed: leave,
        ),
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(widget.target.server.name, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 4),
          Text(widget.target.baseUrl, style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 16),
          // Two different situations reach this page and they need different words:
          // one has been refused and has to pair, the other is already being served
          // and is choosing to. Telling the second one the server "only serves devices
          // it knows" is simply false.
          Text(widget.target.resumeTo == null ? l10n.pairIntro : l10n.pairVoluntaryIntro),
          const SizedBox(height: 24),
          TextField(
            controller: _deviceName,
            decoration: InputDecoration(
              labelText: l10n.pairDeviceNameLabel,
              border: const OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _code,
            enabled: !_busy && !_waitingForApproval,
            keyboardType: TextInputType.number,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(6),
            ],
            decoration: InputDecoration(
              labelText: l10n.pairCodeLabel,
              border: const OutlineInputBorder(),
            ),
            onSubmitted: (_) => _pairWithCode(),
          ),
          const SizedBox(height: 12),
          FilledButton(
            onPressed: _busy || _waitingForApproval ? null : _pairWithCode,
            child: Text(l10n.pairSubmit),
          ),
          const SizedBox(height: 8),
          TextButton(
            onPressed: _busy || _waitingForApproval ? null : _requestApproval,
            child: Text(l10n.pairAskInstead),
          ),
          if (_waitingForApproval) ...[
            const SizedBox(height: 16),
            const LinearProgressIndicator(),
            const SizedBox(height: 8),
            Text(l10n.pairWaiting),
          ],
          if (_error != null && _error!.isNotEmpty) ...[
            const SizedBox(height: 16),
            Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      ),
    );
  }
}
