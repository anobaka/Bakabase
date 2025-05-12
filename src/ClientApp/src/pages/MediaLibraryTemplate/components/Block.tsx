import { AiOutlineBlock } from 'react-icons/ai';
import { Button, Divider } from '@/components/bakaui';

type Props = {
  title: any;
  description?: any;
  icon?: any;
  onIconPress?: any;
  children: any;
};

export default ({ title, description, icon, onIconPress, children }: Props) => {
  return (
    <div>
      <div>
        <div className={'flex items-center gap-1'}>
          <AiOutlineBlock className={'text-lg'} />
          <div className={'text-medium'}>{title}</div>
          {icon && onIconPress ? (
            <Button
              isIconOnly
              variant={'light'}
              color={'secondary'}
              size={'sm'}
              onPress={onIconPress}
            >
              {icon}
            </Button>
          ) : icon}
          <div className={'opacity-60'}>{description}</div>
        </div>
      </div>
      <div className={'flex gap-1'}>
        <Divider className={'h-auto'} orientation={'vertical'} />
        <div className={'grow'}>
          {children}
        </div>
      </div>
    </div>
  );
};
