# Post parser inference policy

Post parsing uses a completion budget of at most 8192 tokens, further reduced by a positive feature
limit or call-specific override. Availability checks can request a smaller budget. Sampling settings
remain configurable. OpenAI-compatible post-parser calls use a five-minute network timeout and no SDK
retries, so a timed-out generation does not immediately repeat expensive computation.

The local Qwen chat-template override is limited to an OpenAI-kind provider whose model ID contains
`qwen` and whose effective endpoint is loopback, a private IPv4 address (10/8, 172.16/12, 192.168/16),
or IPv6 ULA. Those requests send `chat_template_kwargs: {enable_thinking: false}` and top-level
`reasoning_effort: none`. The template switch is necessary: `reasoning_effort` alone did not disable
thinking in the verified llama.cpp deployment. This policy does not change the inference server.
Unrecognized endpoints/models and other provider kinds receive no added thinking parameters; their
reasoning behavior remains provider-defined. Other AI features retain their original transport policy.

The template kwargs remain serializable request options at the cache boundary. An inner SDK adapter
copies them into the outgoing JSON for both normal and streaming calls, preserving other caller
template arguments and raw SDK options. Cache identity includes the kwargs and all serializable
options/messages. Identical concurrent cache misses recheck the cache after taking a shared keyed
gate. Cancelled waiters do not cancel another request; unused gates are removed immediately. Truncated
outputs, request failures and invalid requested JSON are not cached. Opaque caller SDK factories and
executable tools bypass caching because their effective settings cannot be safely identified.
