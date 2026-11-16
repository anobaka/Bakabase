import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineLink } from "react-icons/ai";
import { Button, Input, Modal, Select, Spinner } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import { postTaskId } from "../AcquisitionCreationOrigin";
import { downloadMethodLabel } from "../methodLabels";
import type { PlanTask } from "./models";
import { postImageSourceApi } from "./postImageSourceApi";

export type ReviewedImageSource = { url: string; methodId: string; accessCode?: string | null };
type Props = {
  origins?: PlanTask["creationOrigins"];
  imageUrl?: string;
  initialAccessCode?: string | null;
  disabled?: boolean;
  onUse: (source: ReviewedImageSource) => Promise<void> | void;
};
type ImageList = Awaited<ReturnType<typeof postImageSourceApi.list>>;

function fileData(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1]);
    reader.onerror = () => reject(new Error("imageUnavailable"));
    reader.readAsDataURL(file);
  });
}

function ImagePicker({
  postId,
  imageUrl,
  initialAccessCode,
  onUse,
  onClose,
}: {
  postId: number;
  imageUrl?: string;
  initialAccessCode?: string | null;
  onUse: Props["onUse"];
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [list, setList] = useState<ImageList>();
  const [imageId, setImageId] = useState("");
  const [sources, setSources] = useState<ReviewedImageSource[]>([]);
  const [chosen, setChosen] = useState<ReviewedImageSource>();
  const [accessCode, setAccessCode] = useState(initialAccessCode ?? "");
  const [status, setStatus] = useState("");
  const [busy, setBusy] = useState(false);
  const [saving, setSaving] = useState(false);
  const [reload, setReload] = useState(0);
  const request = useRef<AbortController>();
  const fileInput = useRef<HTMLInputElement>(null);
  const selected = list?.images.find((image) => image.id === imageId);

  useEffect(() => {
    const controller = new AbortController();
    setList(undefined);
    setStatus("");
    setImageId("");
    setSources([]);
    setChosen(undefined);
    void postImageSourceApi
      .list(postId, controller.signal)
      .then((value) => {
        if (controller.signal.aborted) return;
        setList(value);
        setImageId(
          value.images.find((img) => img.url === imageUrl)?.id ??
            (value.images.length === 1 ? value.images[0].id : ""),
        );
      })
      .catch(() => {
        if (!controller.signal.aborted) setStatus("listUnavailable");
      });
    return () => {
      controller.abort();
      request.current?.abort();
    };
  }, [postId, imageUrl, reload]);

  const reset = () => {
    request.current?.abort();
    setStatus("");
    setSources([]);
    setChosen(undefined);
    setAccessCode(initialAccessCode ?? "");
    setBusy(false);
  };
  const decode = async (file?: File) => {
    if (!list || !selected || saving) return;
    reset();
    if (file && (!file.size || file.size > 4 * 1024 * 1024)) {
      setStatus(file.size ? "imageTooLarge" : "unsupportedImage");
      return;
    }
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    try {
      const data = file ? await fileData(file) : undefined;
      if (controller.signal.aborted) return;
      const result = await postImageSourceApi.decode(
        postId,
        list.revision,
        selected.id,
        controller.signal,
        data,
      );
      if (controller.signal.aborted) return;
      const values = result.sources ?? [];
      setSources(values);
      setChosen(values.length === 1 ? values[0] : undefined);
      setStatus(result.status === "decoded" && values.length ? "" : result.status);
    } catch {
      if (!controller.signal.aborted) setStatus("imageUnavailable");
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  };
  const useSelected = async () => {
    if (!chosen || busy || saving) return;
    setSaving(true);
    try {
      await onUse({ url: chosen.url, methodId: chosen.methodId, accessCode });
      onClose();
    } catch (error) {
      const code = error instanceof Error ? error.message : "";
      setStatus(
        code.startsWith("SOURCE_REPLACEMENT_DUPLICATE")
          ? "duplicate"
          : /PLAN_(VERSION_CONFLICT|ALREADY_STARTED)|SOURCE_REPLACEMENT_LOCAL_FILES/.test(code)
            ? "taskChanged"
            : "saveFailed",
      );
    } finally {
      setSaving(false);
    }
  };

  const choose = (source: ReviewedImageSource, suggestedCode?: string | null) => {
    request.current?.abort();
    setBusy(false);
    setStatus("");
    setAccessCode(source.accessCode ?? suggestedCode ?? "");
    setChosen(source);
  };
  const choice = (source: ReviewedImageSource, floor?: number, context?: string | null) => (
    <Button
      key={source.url}
      size="sm"
      variant="flat"
      className="h-auto w-full justify-start py-2"
      aria-pressed={chosen === source}
      color={chosen === source ? "primary" : "default"}
      isDisabled={saving}
      onPress={() => choose(source, floor === undefined ? initialAccessCode : undefined)}
    >
      <span className="min-w-0 whitespace-normal text-start [overflow-wrap:anywhere]">
        {context && <span className="block font-medium">{context}</span>}
        {downloadMethodLabel({ id: source.methodId, displayName: source.methodId }, t)} ·{" "}
        {source.url}
        {floor !== undefined && (
          <span className="ms-2 text-default-500">
            {floor > 0
              ? t("acquisition.sourceImage.floor", { floor })
              : t("acquisition.origin.postBody")}
          </span>
        )}
      </span>
    </Button>
  );
  return (
    <Modal
      visible
      size="2xl"
      title={t("acquisition.sourceImage.title")}
      onClose={() => {
        if (!saving) onClose();
      }}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      footer={
        <>
          <Button variant="light" isDisabled={saving} onPress={onClose}>
            {t("common.action.close")}
          </Button>
          <Button
            color="primary"
            isDisabled={!chosen || busy || saving}
            isLoading={saving}
            onPress={() => void useSelected()}
          >
            {t("acquisition.sourceImage.use")}
          </Button>
        </>
      }
    >
      {!list && !status && <Spinner size="sm" aria-label={t("common.state.loading")} />}
      {list && !list.images.length && !list.links?.length && (
        <p role="status">{t("acquisition.sourceImage.empty")}</p>
      )}
      {!!list?.links?.length && (
        <section aria-label={t("acquisition.sourceImage.postLinks")} className="space-y-2">
          <p className="text-sm font-medium">{t("acquisition.sourceImage.postLinks")}</p>
          <div className="max-h-48 space-y-1 overflow-y-auto">
            {list.links.map((source) => choice(source, source.floor, source.context))}
          </div>
        </section>
      )}
      {!!list?.images.length && (
        <>
          <Select
            label={t("acquisition.sourceImage.image")}
            selectedKeys={imageId ? [imageId] : []}
            isDisabled={saving}
            dataSource={list.images.map((image, index) => ({
              value: image.id,
              label: t("acquisition.sourceImage.imageLabel", {
                number: index + 1,
                location:
                  image.floor > 0
                    ? t("acquisition.sourceImage.floor", { floor: image.floor })
                    : t("acquisition.origin.postBody"),
              }),
              description: image.context || image.url,
            }))}
            onSelectionChange={(keys) => {
              reset();
              setImageId(String(Array.from(keys)[0] ?? ""));
            }}
          />
          {selected && (
            <div className="space-y-2">
              {selected.context && (
                <p className="text-sm text-default-600 break-words">{selected.context}</p>
              )}
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  size="sm"
                  variant="flat"
                  color="primary"
                  isDisabled={saving || busy}
                  isLoading={busy}
                  onPress={() => void decode()}
                >
                  {t("acquisition.sourceImage.decode")}
                </Button>
                <ExternalLink href={selected.url} className="text-sm">
                  {t("acquisition.sourceImage.openImage")}
                </ExternalLink>
                <Button
                  size="sm"
                  variant="light"
                  isDisabled={saving || busy}
                  onPress={() => fileInput.current?.click()}
                >
                  {t("acquisition.sourceImage.local")}
                </Button>
                <input
                  ref={fileInput}
                  type="file"
                  className="hidden"
                  accept="image/png,image/jpeg,image/webp,image/gif,image/bmp"
                  onChange={(event) => {
                    const file = event.target.files?.[0];
                    event.target.value = "";
                    if (file) void decode(file);
                  }}
                />
              </div>
            </div>
          )}
        </>
      )}
      {!!sources.length && (
        <div className="space-y-2">
          <p className="text-sm font-medium">{t("acquisition.sourceImage.found")}</p>
          {sources.map((source) => choice(source))}
        </div>
      )}
      {chosen && (
        <Input
          size="sm"
          label={t("acquisition.draft.accessCode")}
          maxLength={512}
          value={accessCode}
          onValueChange={setAccessCode}
          isDisabled={saving}
        />
      )}
      {status && (
        <div className="flex flex-wrap items-center gap-2 text-sm text-warning-600" role="alert">
          {t(`acquisition.sourceImage.status.${status}`, {
            defaultValue: t("acquisition.sourceImage.status.imageUnavailable"),
          })}
          {(!list || status === "sourceChanged") && (
            <Button size="sm" variant="flat" onPress={() => setReload((n) => n + 1)}>
              {t("common.action.retry")}
            </Button>
          )}
        </div>
      )}
    </Modal>
  );
}

export default function PostImageSourceAction({
  origins,
  imageUrl,
  initialAccessCode,
  disabled,
  onUse,
}: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const posts = (origins ?? [])
    .flatMap((origin) => {
      const id = postTaskId(origin);
      return id != null && origin.sourceState === "available" ? [{ id, title: origin.title }] : [];
    })
    .filter((p, i, all) => all.findIndex((other) => other.id === p.id) === i);
  const [chosenPost, setChosenPost] = useState<number>();
  const postId = posts.some((post) => post.id === chosenPost) ? chosenPost : posts[0]?.id;
  if (postId == null) return null;
  return (
    <>
      {posts.length > 1 && (
        <Select
          size="sm"
          label={t("acquisition.origin.label")}
          selectedKeys={[String(postId)]}
          isDisabled={disabled}
          dataSource={posts.map((post) => ({
            value: String(post.id),
            label: post.title || "#" + post.id,
          }))}
          onSelectionChange={(keys) => setChosenPost(Number(Array.from(keys)[0]))}
        />
      )}
      <Button
        size="sm"
        variant="flat"
        isDisabled={disabled}
        startContent={<AiOutlineLink aria-hidden />}
        onPress={() => setOpen(true)}
      >
        {t("acquisition.sourceImage.title")}
      </Button>
      {open && (
        <ImagePicker
          key={postId}
          postId={postId}
          imageUrl={imageUrl}
          initialAccessCode={initialAccessCode}
          onUse={onUse}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}
