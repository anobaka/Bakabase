import { useLocalResourceDeepLink } from "./useLocalResourceDeepLink";

import ResourcePage from "./index";

/** Navigation adapter; the original local resource page retains its existing stores and behavior. */
export default function ResourceEntry() {
  useLocalResourceDeepLink();

  return <ResourcePage />;
}
