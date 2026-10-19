# Post parser inference policy

Post parsing uses a completion budget of at most 16384 tokens, further reduced by a positive feature
limit or call-specific override. Availability checks can request a smaller budget. Sampling settings
remain configurable. OpenAI-compatible post-parser calls use a five-minute network timeout and no SDK
retries, so a timed-out generation does not immediately repeat expensive computation.

The local Qwen chat-template override is limited to an OpenAI-kind provider whose model ID contains
`qwen` and whose effective endpoint is loopback, a private IPv4 address (10/8, 172.16/12, 192.168/16),
or IPv6 ULA. To retain extraction accuracy, those requests default to top-level `reasoning_effort: low`
and `chat_template_kwargs: {enable_thinking: true, reasoning_effort: low}`. The template effort explicitly
overrides higher CLI/saved defaults without imposing a hard reasoning-token budget. Internal callers
can explicitly select None (including `ChatOptions.Reasoning.Effort = None`); that mode sends top-level
`reasoning_effort: none` and `chat_template_kwargs: {enable_thinking: false}`. The template switch is
necessary because `reasoning_effort` alone did not disable thinking in the verified llama.cpp deployment.
The policy does not change the inference server.
Unrecognized endpoints/models and other provider kinds receive no added thinking parameters; their
reasoning behavior remains provider-defined. Other AI features retain their original transport policy.

For the same local Qwen targets, plain JSON mode becomes a nonempty `json_schema` with schema
`{type: object}` and name `post_analysis`. The verified llama.cpp Qwen handler skips JSON grammar for
the empty schema produced by `json_object`; a nonempty schema activates constraints while allowing
arbitrary object fields. The inner adapter preserves the schema without OpenAI strict-schema
rewrites. Existing caller schemas, text mode and unrecognized providers retain their selected formats.
Backend callers can supply `LlmModelParameters.JsonResponseSchema` for these local Qwen JSON requests;
the feature override preserves the full schema and includes it in cache identity. Other providers keep
their existing plain JSON behavior, and a schema without `UseJsonResponseFormat` does not enable JSON mode.
See the verified [llama.cpp Qwen response-format handler](https://github.com/ggml-org/llama.cpp/blob/925e11799/common/chat.cpp#L1199).

The template kwargs remain serializable request options at the cache boundary. An inner SDK adapter
copies them into the outgoing JSON for both normal and streaming calls, preserving other caller
template arguments and raw SDK options. Cache identity includes the kwargs and all serializable
options/messages. Identical concurrent cache misses recheck the cache after taking a shared keyed
gate. Cancelled waiters do not cancel another request; unused gates are removed immediately. Truncated
outputs, request failures and invalid requested JSON are not cached. Opaque caller SDK factories and
executable tools bypass caching because their effective settings cannot be safely identified.
