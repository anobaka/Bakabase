import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input, Select, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { PythonRuntimeMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { useDependencyChecks } from "./dependencyChecks";

type Configuration = Parameters<typeof BApi.pythonRuntime.validatePythonRuntimeConfiguration>[0];
type ConfigurationView = NonNullable<
  Awaited<ReturnType<typeof BApi.pythonRuntime.getPythonRuntimeConfiguration>>["data"]
>;
type Candidate = NonNullable<
  Awaited<ReturnType<typeof BApi.pythonRuntime.getPythonRuntimeCandidates>>["data"]
>[number];
type Validation = NonNullable<
  Awaited<ReturnType<typeof BApi.pythonRuntime.validatePythonRuntimeConfiguration>>["data"]
>;

export default function PythonRuntimeConfiguration() {
  const { t } = useTranslation();
  const serverId = useRemoteAccessStore((state) => state.serverId);
  const { checkDependencies } = useDependencyChecks();
  const [current, setCurrent] = useState<ConfigurationView>();
  const [draft, setDraft] = useState<Configuration>({ mode: PythonRuntimeMode.Managed });
  const [candidates, setCandidates] = useState<Candidate[]>([]);
  const [discovered, setDiscovered] = useState(false);
  const [validation, setValidation] = useState<Validation>();
  const [busy, setBusy] = useState<"load" | "discover" | "validate" | "apply">();
  const [error, setError] = useState<string>();
  const [applied, setApplied] = useState(false);
  const [reload, setReload] = useState(0);
  const epoch = useRef(0);
  const revision = useRef(0);
  const load = useCallback(async () => {
    const response = await BApi.pythonRuntime.getPythonRuntimeConfiguration();
    if (response.code || !response.data)
      throw new Error(response.message || "dependencies.python.loadFailed");
    return response.data;
  }, [serverId]);
  useEffect(() => {
    const request = ++epoch.current;
    setBusy("load");
    setCurrent(undefined);
    setError(undefined);
    setCandidates([]);
    setDiscovered(false);
    setValidation(undefined);
    setApplied(false);
    void load()
      .then((value) => {
        if (request === epoch.current) {
          setCurrent(value);
          setDraft(value.configuration);
        }
      })
      .catch((failure: unknown) => {
        if (request === epoch.current)
          setError(failure instanceof Error ? failure.message : String(failure));
      })
      .finally(() => {
        if (request === epoch.current) setBusy(undefined);
      });
    return () => {
      epoch.current++;
    };
  }, [load, reload]);
  const change = (value: Configuration) => {
    revision.current++;
    setDraft(value);
    setValidation(undefined);
    setError(undefined);
    setApplied(false);
  };
  const perform = async (operation: "discover" | "validate" | "apply") => {
    if (busy) return;
    const request = epoch.current;
    const draftRevision = revision.current;
    const active = () => request === epoch.current && draftRevision === revision.current;
    setBusy(operation);
    setError(undefined);
    setApplied(false);
    try {
      if (operation === "discover") {
        const response = await BApi.pythonRuntime.getPythonRuntimeCandidates();
        if (!active()) return;
        if (response.code)
          throw new Error(response.message || t("dependencies.python.discoveryFailed"));
        setCandidates(response.data ?? []);
        setDiscovered(true);
      } else {
        const response =
          operation === "apply"
            ? await BApi.pythonRuntime.applyPythonRuntimeConfiguration(draft)
            : await BApi.pythonRuntime.validatePythonRuntimeConfiguration(draft);
        if (!active()) return;
        if (response.code || !response.data)
          throw new Error(response.message || t("dependencies.python.operationFailed"));
        setValidation(response.data);
        if (!response.data.isValid || (operation === "apply" && !response.data.applied))
          throw new Error(response.data.error || t("dependencies.python.operationFailed"));
        if (operation === "apply") {
          setCurrent({
            configuration: response.data.configuration,
            runtime: response.data.runtime,
          });
          setDraft(response.data.configuration);
          setApplied(true);
          void checkDependencies([
            "cloud-python-component-service",
            "gdown-component-service",
            "gallery-dl-component-service",
          ]);
        }
      }
    } catch (failure) {
      if (active())
        setError(
          failure instanceof Error ? failure.message : t("dependencies.python.operationFailed"),
        );
    } finally {
      if (request === epoch.current) setBusy(undefined);
    }
  };
  const modes = [PythonRuntimeMode.Managed, PythonRuntimeMode.External, PythonRuntimeMode.Conda];
  const complete =
    draft.mode === PythonRuntimeMode.Managed ||
    (draft.mode === PythonRuntimeMode.External
      ? !!draft.pythonExecutable?.trim()
      : !!draft.condaExecutable?.trim() && !!draft.condaPrefix?.trim());
  return (
    <section
      className="space-y-4 rounded-xl border border-default-200 p-4"
      aria-label={t("dependencies.python.configuration")}
    >
      <h3 className="text-sm font-semibold">{t("dependencies.python.configuration")}</h3>
      <p className="text-xs leading-relaxed text-default-500">
        {t("dependencies.python.configurationHint")}
      </p>
      {busy === "load" ? (
        <Spinner size="sm" />
      ) : (
        <>
          {current && (
            <div className="space-y-1 rounded-lg bg-default-50 p-3 text-xs">
              <p>
                {t("dependencies.python.activeSource")}:{" "}
                {t(`dependencies.python.mode.${current.configuration.mode}`)}
              </p>
              {current.runtime && (
                <>
                  <p className="font-mono">
                    Python {current.runtime.pythonVersion} · {current.runtime.architecture}
                  </p>
                  <p className="break-all font-mono text-default-500">
                    {current.runtime.executable}
                  </p>
                </>
              )}
              {current.error && <p className="break-words text-warning-600">{current.error}</p>}
            </div>
          )}
          <Select
            label={t("dependencies.python.source")}
            isDisabled={!!busy}
            selectedKeys={[String(draft.mode)]}
            disallowEmptySelection
            dataSource={modes.map((mode) => ({
              value: String(mode),
              label: t(`dependencies.python.mode.${mode}`),
            }))}
            onSelectionChange={(keys) => {
              const selected = String(Array.from(keys)[0]);
              const mode = modes.find((candidate) => String(candidate) === selected);
              if (mode !== undefined) change({ mode });
            }}
          />
          {draft.mode === PythonRuntimeMode.Managed ? (
            <p className="text-xs text-default-500">{t("dependencies.pythonAutomatic")}</p>
          ) : draft.mode === PythonRuntimeMode.External ? (
            <Input
              isDisabled={!!busy}
              label={t("dependencies.python.executable")}
              placeholder="/usr/bin/python3"
              value={draft.pythonExecutable ?? ""}
              onValueChange={(pythonExecutable) => change({ ...draft, pythonExecutable })}
            />
          ) : (
            <>
              <Input
                isDisabled={!!busy}
                label={t("dependencies.python.condaExecutable")}
                placeholder="/opt/conda/bin/conda"
                value={draft.condaExecutable ?? ""}
                onValueChange={(condaExecutable) => change({ ...draft, condaExecutable })}
              />
              <Input
                isDisabled={!!busy}
                label={t("dependencies.python.condaPrefix")}
                placeholder="/opt/conda/envs/bakabase"
                value={draft.condaPrefix ?? ""}
                onValueChange={(condaPrefix) => change({ ...draft, condaPrefix })}
              />
            </>
          )}
          <Button
            size="sm"
            variant="flat"
            isDisabled={!!busy}
            isLoading={busy === "discover"}
            onPress={() => void perform("discover")}
          >
            {t("dependencies.python.discover")}
          </Button>
          {candidates.length > 0 && (
            <Select
              label={t("dependencies.python.candidates")}
              isDisabled={!!busy}
              selectedKeys={[]}
              dataSource={candidates.map((candidate, index) => ({
                value: String(index),
                label: candidate.label,
              }))}
              onSelectionChange={(keys) => {
                const candidate = candidates[Number(Array.from(keys)[0])];
                if (candidate) change(candidate.configuration);
              }}
            />
          )}
          {discovered && candidates.length === 0 && (
            <p role="status" className="text-xs text-default-500">
              {t("dependencies.python.noCandidates")}
            </p>
          )}
          <div className="flex flex-wrap gap-2">
            <Button
              size="sm"
              variant="flat"
              isDisabled={!!busy || !complete || !current}
              isLoading={busy === "validate"}
              onPress={() => void perform("validate")}
            >
              {t("dependencies.python.validate")}
            </Button>
            <Button
              size="sm"
              color="primary"
              isDisabled={!!busy || !complete || !current}
              isLoading={busy === "apply"}
              onPress={() => void perform("apply")}
            >
              {t(
                draft.mode === PythonRuntimeMode.Managed
                  ? "dependencies.python.applyManaged"
                  : "dependencies.python.apply",
              )}
            </Button>
          </div>
          {busy === "apply" && (
            <p role="status" className="text-xs text-primary">
              {t("dependencies.python.applying")}
            </p>
          )}
          {validation?.isValid && !error && busy !== "apply" && (
            <div className="space-y-1 text-xs">
              <p role="status" className="text-success">
                {t(applied ? "dependencies.python.applied" : "dependencies.python.valid")}
              </p>
              {!applied && validation.runtime && (
                <p className="break-all font-mono text-default-500">
                  Python {validation.runtime.pythonVersion} · {validation.runtime.architecture}
                  <br />
                  {validation.runtime.executable}
                </p>
              )}
            </div>
          )}
        </>
      )}
      {error && (
        <div role="alert" className="space-y-1 text-xs text-danger">
          <p className="break-words">{t(error, { defaultValue: error })}</p>
          <p>{t("dependencies.python.unchanged")}</p>
          {!current && (
            <Button size="sm" variant="light" onPress={() => setReload((value) => value + 1)}>
              {t("common.action.retry")}
            </Button>
          )}
        </div>
      )}
    </section>
  );
}
