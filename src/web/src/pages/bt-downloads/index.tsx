import { Navigate, useLocation } from "react-router-dom";
import { btDownloadRedirect } from "@/pages/downloader/downloadCenter";

/** Keep saved links and result-detail links pointing at the same task identity. */
export default function BtDownloadsPage() {
  const location = useLocation();
  return <Navigate replace to={btDownloadRedirect(location.search)} />;
}
