import { useTranslation } from 'react-i18next';
import React, { useEffect, useState } from 'react';
import { DeleteOutlined } from '@ant-design/icons';
import { Button, Chip, Divider, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ProcessModal from '@/pages/BulkModification2/components/BulkModification/ProcessModal';
import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import ProcessStep from '@/pages/BulkModification2/components/BulkModification/ProcessStep';
import { PropertyPool } from '@/sdk/constants';

type Props = {
  processes?: BulkModificationProcess[];
  variables?: BulkModificationVariable[];
  onChange?: (processes: BulkModificationProcess[]) => void;
};

export default ({
                  processes: propsProcesses,
                  onChange,
                  variables,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [processes, setProcesses] = useState<BulkModificationProcess[]>(propsProcesses ?? []);

  useEffect(() => {
  }, []);

  return (
    <div className={'grow'}>
      {processes.length > 0 && (
        <div className={'flex flex-col gap-1 mb-2'}>
          {processes.map((process, i) => {
            return (
              <React.Fragment key={i}>
                <div
                  className={'flex gap-1 cursor-pointer hover:bg-[var(--bakaui-overlap-background)] rounded items-center'}
                  onClick={() => {
                    createPortal(ProcessModal, {
                      process: process,
                      variables: variables,
                      onSubmit: (p) => {
                        processes[i] = p;
                        const nps = [...processes];
                        setProcesses(nps);
                        onChange?.(nps);
                      },
                    });
                  }}
                >
                  <div className={'flex items-center gap-1'}>
                    <Chip
                      size={'sm'}
                      radius={'sm'}
                    >{i + 1}</Chip>
                    <div>
                      <Chip
                        size={'sm'}
                        radius={'sm'}
                        variant={'flat'}
                        color={'secondary'}
                      >{process.property?.poolName}</Chip>
                      <Chip
                        size={'sm'}
                        radius={'sm'}
                        variant={'light'}
                        color={'primary'}
                      >
                        {process.property?.name}
                      </Chip>
                    </div>
                    <Button
                      size={'sm'}
                      variant={'light'}
                      color={'danger'}
                      className={'min-w-fit px-2'}
                      onClick={() => {
                        createPortal(
                          Modal, {
                            defaultVisible: true,
                            title: t('Delete a process'),
                            children: t('Are you sure you want to delete this process?'),
                            onOk: async () => {
                              const nps = processes.filter((_, j) => j !== i);
                              setProcesses(nps);
                              onChange?.(nps);
                            },
                          },
                        );
                      }}
                    >
                      <DeleteOutlined className={'text-base'} />
                    </Button>
                  </div>
                  <div className={'pl-2 flex flex-col gap-1'}>
                    {process.steps?.map((step, j) => {
                      return (
                        <ProcessStep
                          step={step}
                          editable={false}
                          property={process.property}
                          variables={variables}
                          no={`${i + 1}.${j + 1}`}
                          onDelete={() => {
                            process.steps?.splice(i, 1);
                            setProcesses([...processes]);
                            onChange?.(processes);
                          }}
                        />
                      );
                    })}
                  </div>
                </div>
                {i != processes.length - 1 && (
                  <div className={'px-2'}>
                    <Divider
                      orientation={'horizontal'}
                    />
                  </div>
                )}
              </React.Fragment>
            );
          })}
        </div>
      )}
      <div>
        <Button
          size={'sm'}
          color={'primary'}
          variant={'ghost'}
          onClick={() => {
            createPortal(ProcessModal, {
              variables: variables,
              onSubmit: (p) => {
                const nps = [...processes, p];
                setProcesses(nps);
                onChange?.(nps);
              },
            });
          }}
        >
          {t('Add')}
        </Button>
      </div>
    </div>
  );
};
