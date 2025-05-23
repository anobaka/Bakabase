import { AiOutlineBlock } from 'react-icons/ai';
import { Button, Divider } from '@/components/bakaui';

type Props = {
  title: any;
  description?: any;
  leftIcon?: any;
  rightIcon?: any;
  onRightIconPress?: any;
  children: any;
};

export default ({ leftIcon, title, description, rightIcon, onRightIconPress, children }: Props) => {
  return (
    <div>
      <div>
        <div className={'flex items-center gap-1'}>
          {leftIcon ?? <AiOutlineBlock className={'text-lg'} />}
          <div className={'text-medium'}>{title}</div>
          {rightIcon && onRightIconPress ? (
            <Button
              isIconOnly
              variant={'light'}
              color={'secondary'}
              size={'sm'}
              onPress={onRightIconPress}
            >
              {rightIcon}
            </Button>
          ) : rightIcon}
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
