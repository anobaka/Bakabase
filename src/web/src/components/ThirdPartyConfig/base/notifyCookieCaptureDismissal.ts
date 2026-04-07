import { toast } from "@/components/bakaui";

/**
 * When cookie capture ends without a cookie (user cancelled or unsupported), the API returns
 * code 0 with no data and a localized message — show a neutral toast instead of an error.
 */
export function notifyCookieCaptureDismissal(rsp: {
  code?: number;
  data?: string | null;
  message?: string | null;
}): boolean {
  if (rsp.code) {
    return false;
  }
  if (rsp.data) {
    return false;
  }
  if (rsp.message) {
    toast.default(rsp.message);
    return true;
  }
  return false;
}
