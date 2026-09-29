import type { ReactNode } from "react";
import type { BakabaseServiceModelsViewRemoteAccessAddressViewModel as RemoteAccessAddress } from "@/sdk/Api";

import AddressList from "./AddressList";

/**
 * Everything the other device types, side by side: this device's address, and a code with
 * the button that makes one. Management hands out pairing codes, library sharing share
 * codes; the block is the same, only the words and the code differ.
 *
 * A new code is said as it appears ("Share code 123456, expires at …"): the button that
 * made it keeps focus and its own name, so without a live region the digits would appear
 * in silence. Only `status` is said with it, so it must not tick; a countdown goes in `note`.
 *
 * Laid out against the page's container (`@3xl`), not the window: beside the app's sidebar a
 * window wide enough for two columns can leave the content too narrow for them.
 */
export default function InviteBlock({
  addresses,
  addressesError,
  onRetryAddresses,
  target,
  context,
  tip,
  code,
  codeLabel,
  status,
  note,
  action,
}: {
  /** Undefined until remote access's settings are read; see `addressesError`. */
  addresses: readonly RemoteAccessAddress[] | undefined;
  /** Why remote access's settings could not be read, with a way to try again. */
  addressesError?: Error;
  onRetryAddresses?: () => void;
  target: string;
  context: "manage" | "sharing";
  /** What the code is for and how long it lasts. */
  tip: string;
  /** The digits, while they still work. */
  code?: string;
  /** What the code is called ("Share code", "Pairing code"), said before its digits. */
  codeLabel: string;
  /** Until when the code works, or that it expired: said with the code, so never a countdown. */
  status?: ReactNode;
  /** Shown under it without being said: a countdown, a code outstanding from elsewhere. */
  note?: ReactNode;
  /** The button that makes a code. */
  action: ReactNode;
}) {
  return (
    <div className="grid gap-3 @3xl:grid-cols-2">
      <div className="rounded-lg border border-default-200 p-3">
        <AddressList
          addresses={addresses}
          context={context}
          error={addressesError}
          target={target}
          variant="compact"
          onRetry={onRetryAddresses}
        />
      </div>
      <div className="space-y-2 rounded-lg border border-default-200 p-3">
        <p className="text-xs text-default-500">{tip}</p>
        {/* Always there, even empty: a live region added with its content is not said. */}
        <div aria-atomic="true" className="space-y-2" data-testid="invite-code" role="status">
          {code && (
            <p>
              <span className="sr-only">{codeLabel} </span>
              <code className="block text-2xl tracking-[0.25em]">{code}</code>
            </p>
          )}
          {status}
        </div>
        {note}
        {action}
      </div>
    </div>
  );
}
