"use client";

import type { InputProps, SnippetProps } from "@heroui/react";
import type { PathAutocompleteProps } from "@/components/PathAutocomplete";

import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { QuestionCircleOutlined } from "@ant-design/icons";

import Component from "./components/Component";

import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { useThirdPartyOptionsStore, useAiOptionsStore } from "@/stores/options";
import {
  Input,
  Popover,
  Snippet,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { EditableValue } from "@/components/EditableValue";
import PathAutocomplete from "@/components/PathAutocomplete";
const Dependency = () => {
  const { t } = useTranslation();
  const componentContexts = useDependentComponentContextsStore(
    (state) => state.contexts,
  );
  const thirdPartyOptions = useThirdPartyOptionsStore((state) => state.data);
  const aiOptions = useAiOptionsStore((state) => state.data);

  useEffect(() => {}, []);

  return (
    <div className="group">
      {/* <Title title={t<string>('Dependent components')} /> */}
      <div className="settings">
        <Table removeWrapper>
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("Dependent components")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            <>
              {componentContexts.map((c, i) => {
                return (
                  <TableRow
                    key={i}
                    className={"hover:bg-[var(--bakaui-overlap-background)]"}
                  >
                    <TableCell>
                      <div className={"flex gap-1 items-center"}>
                        {c.name}
                        <Popover
                          // color={'primary'}
                          showArrow
                          placement={"right"}
                          trigger={
                            <QuestionCircleOutlined className={"text-base"} />
                          }
                        >
                          <div
                            className={"px-2 py-4 flex flex-col gap-2"}
                            style={{ userSelect: "text" }}
                          >
                            {c.description && <pre>{c.description}</pre>}
                            <div className={"flex items-center gap-2"}>
                              {t<string>("Default location")}
                              <Snippet
                                hideSymbol
                                size={"sm"}
                                variant="bordered"
                              >
                                {c.defaultLocation}
                              </Snippet>
                            </div>
                          </div>
                        </Popover>
                      </div>
                    </TableCell>
                    <TableCell>
                      <Component id={c.id} />
                    </TableCell>
                  </TableRow>
                );
              })}
            </>
            <TableRow
              key={componentContexts.length}
              className={"hover:bg-[var(--bakaui-overlap-background)]"}
            >
              <TableCell>{t<string>("curl executable")}</TableCell>
              <TableCell>
                <EditableValue<
                  string,
                  PathAutocompleteProps,
                  SnippetProps & { value: string }
                >
                  Editor={({ onValueChange, value, ...props }) => (
                    <PathAutocomplete
                      {...props}
                      pathType={"file"}
                      value={value as string}
                      onChange={onValueChange}
                    />
                  )}
                  Viewer={({ value, ...props }) =>
                    value ? (
                      <Snippet symbol={<>&nbsp;</>} {...props} size={"sm"}>
                        {value}
                      </Snippet>
                    ) : null
                  }
                  editorProps={{
                    placeholder: t<string>("path/to/curl.exe"),
                    size: "sm",
                  }}
                  value={thirdPartyOptions.curlExecutable}
                  onSubmit={async (v) =>
                    await BApi.options.putThirdPartyOptions({
                      ...thirdPartyOptions,
                      curlExecutable: v,
                    })
                  }
                />
              </TableCell>
            </TableRow>
            <TableRow
              key={componentContexts.length + 1}
              className={"hover:bg-[var(--bakaui-overlap-background)]"}
            >
              <TableCell>{t<string>("Ollama endpoint")}</TableCell>
              <TableCell>
                <EditableValue<
                  string,
                  InputProps,
                  SnippetProps & { value: string }
                >
                  Editor={Input}
                  Viewer={({ value, ...props }) =>
                    value ? (
                      <Snippet symbol={<>&nbsp;</>} {...props} size={"sm"}>
                        {value}
                      </Snippet>
                    ) : null
                  }
                  editorProps={{
                    placeholder: t<string>("http://localhost:11434"),
                    size: "sm",
                  }}
                  value={aiOptions.ollamaEndpoint}
                  onSubmit={async (v) =>
                    await BApi.options.putAiOptions({
                      ...aiOptions,
                      ollamaEndpoint: v,
                    })
                  }
                />
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
        {/* <Table */}
        {/*   dataSource={componentContexts} */}
        {/*   size={'small'} */}
        {/*   hasHeader={false} */}
        {/*   cellProps={(r, c) => { */}
        {/*     return { */}
        {/*       className: c == 0 ? 'key' : c == 1 ? 'value' : '', */}
        {/*     }; */}
        {/*   }} */}
        {/* > */}
        {/*   <Table.Column */}
        {/*     width={300} */}
        {/*     dataIndex={'name'} */}
        {/*     cell={(name, i, c) => { */}
        {/*       return ( */}
        {/*         <> */}
        {/*           {name} */}
        {/*           <Balloon */}
        {/*             trigger={( */}
        {/*               <CustomIcon size={'small'} type={'question-circle'} /> */}
        {/*             )} */}
        {/*             triggerType={'click'} */}
        {/*             align={'r'} */}
        {/*           > */}
        {/*             <div style={{ userSelect: 'text' }}> */}
        {/*               {c.description && ( */}
        {/*                 <div> */}
        {/*                   {c.description} */}
        {/*                 </div> */}
        {/*               )} */}
        {/*               <div> */}
        {/*                 {t<string>('Default location')}: {c.defaultLocation} */}
        {/*               </div> */}
        {/*             </div> */}

        {/*           </Balloon> */}
        {/*         </> */}
        {/*       ); */}
        {/*     }} */}
        {/*   /> */}
        {/*   <Table.Column */}
        {/*     dataIndex={'id'} */}
        {/*     cell={(id) => ( */}
        {/*       <Component id={id} /> */}
        {/*     )} */}
        {/*   /> */}
        {/* </Table> */}
      </div>
    </div>
  );
};

Dependency.displayName = "Dependency";

export default Dependency;
