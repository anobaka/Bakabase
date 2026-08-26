import { describe, expect, it } from "vitest";

import { describeUpdateError } from "../describeUpdateError";

describe("describeUpdateError", () => {
  it("returns undefined when there is no error", () => {
    expect(describeUpdateError(undefined)).toBeUndefined();
    expect(describeUpdateError("")).toBeUndefined();
    expect(describeUpdateError("   ")).toBeUndefined();
  });

  it("classifies the .NET outer message for a TLS failure as a certificate problem", () => {
    // This exact string is what an expired certificate on the update endpoint
    // surfaces as, and why it gets mistaken for a bad connection.
    const d = describeUpdateError(
      "The SSL connection could not be established, see inner exception.",
    );

    expect(d?.kind).toBe("certificate");
    expect(d?.messageKey).toBe("appUpdate.error.certificate");
  });

  it.each([
    "The remote certificate is invalid because of errors in the certificate chain: NotTimeValid",
    "RemoteCertificateNameMismatch, RemoteCertificateChainErrors",
    "Authentication failed because the remote party has closed the transport stream.",
    "The certificate has expired",
    "UntrustedRoot",
    "PartialChain",
  ])("classifies %s as a certificate problem", (raw) => {
    expect(describeUpdateError(raw)?.kind).toBe("certificate");
  });

  it.each([
    "No such host is known.",
    "Name or service not known",
    "Connection refused",
    "No connection could be made because the target machine actively refused it",
    "The request timed out",
    "Network is unreachable",
    "A task was canceled.",
    "Temporary failure in name resolution",
  ])("classifies %s as a connectivity problem", (raw) => {
    expect(describeUpdateError(raw)?.kind).toBe("connectivity");
  });

  it("prefers the certificate classification when a message mentions both", () => {
    const d = describeUpdateError(
      "The SSL connection could not be established. Unable to connect to the remote server.",
    );

    expect(d?.kind).toBe("certificate");
  });

  it("falls back to unknown but still carries the raw detail", () => {
    const raw = "Something entirely unexpected went wrong";
    const d = describeUpdateError(raw);

    expect(d?.kind).toBe("unknown");
    expect(d?.messageKey).toBe("appUpdate.error.unknown");
    expect(d?.detail).toBe(raw);
  });

  it("always preserves the original message so the real cause is never hidden", () => {
    const raw = "The SSL connection could not be established, see inner exception.";

    expect(describeUpdateError(raw)?.detail).toBe(raw);
  });

  it("matches case-insensitively", () => {
    expect(describeUpdateError("NO SUCH HOST IS KNOWN.")?.kind).toBe("connectivity");
  });
});
