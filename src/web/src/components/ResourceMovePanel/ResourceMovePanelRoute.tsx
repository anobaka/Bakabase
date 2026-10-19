import { useEffect, useLayoutEffect } from "react";
import { matchPath, useLocation, useNavigate } from "react-router-dom";

import ResourceMovePanel from "./index";

import { RESOURCE_MOVE_OPENED_EVENT, resetMovePanelView } from "@/stores/resourceMovePanel";

/** Keep task synchronization global while the panel's view belongs to the resource page. */
export default function ResourceMovePanelRoute() {
  const { pathname } = useLocation();
  const navigate = useNavigate();
  const visible = !!matchPath({ path: "/resource", end: true }, pathname);

  useLayoutEffect(() => {
    if (!visible) resetMovePanelView();

    return () => {
      if (visible) resetMovePanelView();
    };
  }, [visible]);

  useEffect(() => {
    const open = () => {
      if (!visible) navigate("/resource");
    };

    window.addEventListener(RESOURCE_MOVE_OPENED_EVENT, open);

    return () => window.removeEventListener(RESOURCE_MOVE_OPENED_EVENT, open);
  }, [navigate, visible]);

  return <ResourceMovePanel visible={visible} />;
}
