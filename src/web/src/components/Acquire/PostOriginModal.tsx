import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, Spinner } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import BApi from "@/sdk/BApi";
import PostDetails from "@/pages/post-parser/components/PostDetails";
import { getDownloadInfo } from "@/pages/post-parser/results";
import type { PostParserTask } from "@/core/models/PostParserTask";

function storedCredentials(post: PostParserTask) {
  const info = getDownloadInfo(post);
  return (info?.resources ?? []).flatMap((resource, index) => {
    const steps = resource.extraction?.steps;
    const passwords = new Set([resource.password, ...(Array.isArray(steps) ? steps
      .filter(step => step?.op === "extractArchive").map(step => step.password) : [])]
      .filter((value): value is string => typeof value === "string" && value.trim().length > 0));
    if (!passwords.size && !resource.code) return [];
    const group = info?.groups?.find(g => g.id === resource.groupId)?.title;
    return [{ index, group, passwords: [...passwords], accessCode: resource.code }];
  });
}

export default function PostOriginModal({ id, revision, href, onClose }: {
  id: number;
  revision?: string | null;
  href?: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [post, setPost] = useState<PostParserTask>();
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setPost(undefined);
    setFailed(false);
    void BApi.postParser.searchPostParserTasks(
      { locateTaskId: id, pageSize: 1, pageIndex: 1 },
      { signal: controller.signal, sensitiveResponse: true, showErrorToast: false },
    ).then(response => {
      if (controller.signal.aborted) return;
      const found = response.data?.items?.find(row => row.id === id);
      if (response.code || !found) setFailed(true);
      else setPost(found as PostParserTask);
    }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [id, retry]);
  const credentials = post && storedCredentials(post);
  return (
    <Modal visible size="4xl" title={t("acquisition.origin.postTask", { id })}
      onClose={onClose} footer={<Button variant="light" onPress={onClose}>{t("common.action.close")}</Button>}>
      {href && <ExternalLink href={href} className="w-fit text-sm">{t("acquisition.origin.openPost")}</ExternalLink>}
      {failed ? (
        <div role="alert" className="flex items-center gap-2 text-sm">
          {t("acquisition.origin.postUnavailable")}
          <Button size="sm" variant="flat" onPress={() => setRetry(n => n + 1)}>{t("common.action.retry")}</Button>
        </div>
      ) : !post ? <Spinner size="sm" aria-label={t("common.state.loading")} /> : (
        <>
          {post.title && <p className="break-words font-medium">{post.title}</p>}
          {revision && post.revision != null && revision !== String(post.revision) && (
            <p className="text-sm text-warning-600">{t("acquisition.origin.postRevisionChanged", { saved: revision, current: post.revision })}</p>
          )}
          <section aria-label={t("acquisition.origin.parsedPasswords")} className="space-y-1">
            <h4 className="text-sm font-medium">{t("acquisition.origin.parsedPasswords")}</h4>
            {!credentials?.some(item => item.passwords.length) && <p className="text-sm text-default-500">{t("acquisition.origin.noParsedPassword")}</p>}
            {credentials?.map(({ index, group, passwords, accessCode }) => (
              <div key={index} className="flex flex-wrap items-baseline gap-x-3 gap-y-1 text-sm">
                {((credentials.length > 1 || group) && <span>{group || t("acquisition.origin.resourceNumber", { number: index + 1 })}</span>)}
                {passwords.map(password => <span key={password} className="select-text break-all font-mono">{password}</span>)}
                {accessCode && <span className="select-text break-all text-xs text-default-500">{t("acquisition.origin.accessCode", { code: accessCode })}</span>}
              </div>
            ))}
          </section>
          <section aria-label={t("postParser.action.viewContent")} className="min-w-0 whitespace-normal [overflow-wrap:anywhere]">
            <h4 className="mb-2 text-sm font-medium">{t("acquisition.origin.postBody")}</h4>
            <PostDetails task={post} />
          </section>
        </>
      )}
    </Modal>
  );
}
