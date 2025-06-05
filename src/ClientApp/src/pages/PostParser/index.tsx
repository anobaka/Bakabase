import { useTranslation } from 'react-i18next';
import { AiOutlineQuestionCircle } from 'react-icons/ai';
import { Alert, Button, Modal } from '@/components/bakaui';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  return (
    <div>
      <div>
        <div>
          <Button
            size={'sm'}
            variant={'flat'}
            color={'secondary'}
          >
            {t('Start parsing')}
          </Button>
          <Button
            size={'sm'}
            variant={'flat'}
            color={'primary'}
          >
            {t('Add tasks')}
          </Button>
        </div>
        <div>
          <Button
            variant={'light'}
            size={'sm'}
            color={'success'}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                size: 'xl',
                title: t('Instructions for Use'),
                children: (
                  <div>
                    <Alert
                      description={(
                        <div>
                          <div>本功能内部使用curl，如果您的系统级curl低于8.14版本，请先在系统配置中配置正确的curl路径。</div>
                          <div>配置请求间隔暂时无法配置，默认内置间隔为3秒。</div>
                        </div>
                      )}
                      title={'curl'}
                    />
                    <Alert
                      description={(
                        <div>
                          <div>本功能内部使用ollama，您需要先安装并运行ollama，并安装至少1个模型，然后在系统配置中配置ollama的api地址。</div>
                          <div>本功能会优先使用ollama模型列表中最大的模型。</div>
                          <div>目前deepseek-r1:8b,14b,32b已经过测试，可放心使用。</div>
                        </div>
                      )}
                      title={'ollama'}
                    />
                  </div>
                ),
                footer: {
                  actions: ['cancel'],
                },
              });
            }}
          >
            <AiOutlineQuestionCircle className={'text-medium'} />
            {t('Instructions for Use')}
          </Button>
        </div>
      </div>
      <div />
    </div>
  );
};
