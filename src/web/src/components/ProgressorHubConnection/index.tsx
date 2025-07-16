"use client";

import type { HubConnection } from "@microsoft/signalr";

import React, { useEffect, useRef } from "react";
import { Modal } from "@/components/bakaui";
import i18n from "i18next";
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";

import envConfig from "@/config/env";
import { toast } from "@/components/bakaui";
import { sleep } from "@/components/utils";
import type { ComponentType } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";

const progressorHubUri = "/hub/progressor";

enum ProgressorClientAction {
  Start = 1,
  Stop = 2,
  Initialize = 3,
}

enum ProgressorSignalRClientMethod {
  StateChanged = "StateChanged",
  ProgressChanged = "ProgressChanged",
  ErrorOccurred = "ErrorOccurred",
}

enum ProgressorSignalRServerMethod {
  Invoke = "Invoke",
}

enum ProgressorStatus {
  Idle = 1,
  Running = 2,
  Complete = 3,
  Suspended = 4,
}

type IProgressorState = {
  status: ProgressorStatus;
  message: string;
};

type IProgressorProgress = {
  percentage: number;
  elapsedMilliseconds: number;
};

type IHubConnectionStateOptions<TProgress extends IProgressorProgress> = {
  id: string;
  url: string;
  onConnectionStateChange: (state: HubConnectionState) => void;
  onStateChange: (state: IProgressorState) => void;
  onProgressChange: (progress: TProgress) => void;
  onFatalError: (code: number, msg: string) => void;
};

class ProgressorHubConnection<TProgress extends IProgressorProgress> {
  public _id: string;
  public _url: string;
  private _conn: HubConnection;
  private _disposed: boolean = false;
  private _connStatus: HubConnectionState;
  public state: IProgressorState;
  public progress: TProgress;

  private readonly onConnectionStateChange: (state: HubConnectionState) => void;
  private readonly onStateChange: (state: IProgressorState) => void;
  private readonly onProgressChange: (state: TProgress) => void;
  private readonly onFatalError: (code: number, msg: string) => void;

  private _log = (...args) => {
    console.log(`[Progressor:${this._id}]`, ...args);
  };

  private _daemon = async () => {
    while (!this._disposed) {
      const conn = this._conn;

      if (conn.state !== this._connStatus) {
        this.onConnectionStateChange(conn.state);
        this._connStatus = conn.state;
      }
      if (conn.state === HubConnectionState.Disconnected) {
        try {
          await conn.start().then(() => {
            conn.invoke(
              ProgressorSignalRServerMethod.Invoke,
              this._id,
              ProgressorClientAction.Initialize,
              null,
            );
          });
        } catch (e) {
          console.log(e);
        }
      }
      await sleep(1000);
    }
  };

  public connect = () => {
    if (!this._conn) {
      this._log("Initializing");
      const conn = (this._conn = new HubConnectionBuilder()
        .withUrl(this._url)
        .configureLogging(LogLevel.Information)
        .build());

      conn.onreconnecting((e) => {
        this._log("onreconnecting", e);
      });
      conn.onclose((e) => {
        this._log("onclose", e);
      });
      conn.onreconnected((connectionId) => {
        this._log("onreconnected", connectionId);
      });

      const methods = {
        [ProgressorSignalRClientMethod.StateChanged]: (varKey, varState) => {
          if (varKey === this._id) {
            this.state = varState;
            this.onStateChange(varState);
          }
        },
        [ProgressorSignalRClientMethod.ProgressChanged]: (
          varKey,
          varProgress,
        ) => {
          if (varKey === this._id) {
            this.progress = varProgress;
            this.onProgressChange(varProgress);
          }
        },
        [ProgressorSignalRClientMethod.ErrorOccurred]: (varKey, varMessage) => {
          if (varKey == this._id) {
            this.onFatalError(-1, varMessage);
          }
        },
      };

      const self = this;

      Object.keys(methods).map((t) =>
        conn.on(t, function () {
          // self._log(`${t}: `, ...arguments);
          methods[t](...arguments);
        }),
      );

      this._daemon();
    }
  };

  public dispose = async () => {
    if (this._conn) {
      this._disposed = true;
      await this._conn.stop();
      this._log(`connection to hub: ${this._url} disposed`);
    }
  };

  public start = async (params) => {
    this.connect();
    await this._conn.send(
      ProgressorSignalRServerMethod.Invoke,
      this._id,
      ProgressorClientAction.Start,
      params ? JSON.stringify(params) : undefined,
    );
  };

  public stop = async () => {
    this.connect();
    console.log(this._conn);
    const aaa = await this._conn.send(
      ProgressorSignalRServerMethod.Invoke,
      this._id,
      ProgressorClientAction.Stop,
      undefined,
    );

    console.log("sended", aaa);
  };

  constructor(options: IHubConnectionStateOptions<TProgress>) {
    this._id = options.id;
    this._url = options.url;
    this.onConnectionStateChange = options.onConnectionStateChange;
    this.onStateChange = options.onStateChange;
    this.onProgressChange = options.onProgressChange;
    this.onFatalError = options.onFatalError;
  }
}

function useProgressorHubConnection(
  id: string,
  createPortal: <P extends DestroyableProps>(
    C: ComponentType<P>,
    props: P,
  ) => { destroy: () => void; key: string },
  onProgressChange = (progress) => {},
  onStateChange = (state) => {},
  onConnected = (connection) => {},
) {
  const progressorRef = useRef();
  const dialogRef = useRef();

  useEffect(() => {
    const url = `${envConfig.apiEndpoint}${progressorHubUri}`;
    const progressor = new ProgressorHubConnection({
      id,
      url,
      onStateChange,
      onProgressChange,
      onConnectionStateChange: (state: HubConnectionState) => {
        dialogRef.current?.hide();
        if (state != HubConnectionState.Connected) {
          dialogRef.current = createPortal(Modal, {
            defaultVisible: true,
            children: (
              <div style={{ textAlign: "center" }}>
                Hub{i18n.t<string>("Connecting...")}
              </div>
            ),
            size: "auto",
            footer: false,
            className: "hub-connection-dialog",
            centered: true,
            hasMask: false,
          });
        } else {
          toast.success(
            `[${progressorHubUri}]${i18n.t<string>("Hub connected")}`,
          );
        }
      },
      onFatalError: (code: number, msg: string) =>
        createPortal(Modal, {
          defaultVisible: true,
          size: "auto",
          title: i18n.t<string>("Error"),
          children: (
            <pre>
              Code: {code}, message: {msg}
            </pre>
          ),
        }),
    });

    progressor.connect();

    progressorRef.current = progressor;

    return () => {
      if (progressorRef.current) {
        progressorRef.current.dispose();
      }
    };
  }, []);

  return progressorRef;
}

export {
  ProgressorHubConnection,
  type IProgressorProgress,
  type IProgressorState,
  ProgressorStatus,
  useProgressorHubConnection,
};
