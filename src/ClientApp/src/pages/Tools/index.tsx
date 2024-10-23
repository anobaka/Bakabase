import React, { useState } from 'react';
import { Balloon, Button, Grid, Input, Loading, Message, Tab } from '@alifd/next';
import './index.scss';
import i18n from 'i18next';
import JavLibrary from '@/pages/Tools/JavLibrary';
import Title from '@/components/Title';
import FileMover from '@/pages/FileMover';

const { Row, Col } = Grid;

const CompareResultColor = {
  // [CompareResult.Same]: 'green',
  // [CompareResult.MaybeSame]: 'orange',
  // [CompareResult.Inequality]: 'red',
};

export default () => {
  const [analyzeForm, setAnalyzeForm] = useState({
    // fullname1: '[(ほんトいぬ)うそねこ]にゃぁ[CN]',
    fullname2: '',
  });
  const [analyzeResult, setAnalyzeResult] = useState();
  const [rrd, setRrd] = useState();
  const [gfd, setGfd] = useState();
  const [loading, setLoading] = useState(false);

  const updateAnalyzeForm = (func) => {
    func(analyzeForm);
    setAnalyzeForm({ ...analyzeForm });
  };

  // const analyze = () => {
  //   AnalyzeFullname({
  //     model: analyzeForm,
  //   }).invoke((a) => {
  //     if (!a.code) {
  //       setAnalyzeResult(a.data);
  //     }
  //   });
  // };


  return (
    <div className="test-page">
      <Loading fullScreen visible={loading} />
      <Tab
        // size={'small'}
        // shape="wrapped"
        contentClassName="custom-tab-content"
        // activeKey={3}
      >
        {/* <Tab.Item title={i18n.t('Commonly used tools')} key={1}> */}
        {/*   <div className={'commonly-used-tools'}> */}
        {/*     <div className={'tool'}> */}
        {/*       <Title title={'Remove middle part of only-one-folder path'} /> */}
        {/*       <div className="line"> */}
        {/*         <Input */}
        {/*           size={'large'} */}
        {/*           value={rrd} */}
        {/*           style={{ width: 800 }} */}
        {/*           placeholder={i18n.t('Root path')} */}
        {/*           onChange={(v) => setRrd(v)} */}
        {/*         /> */}
        {/*       </div> */}
        {/*       <div className="line"> */}
        {/*         <Balloon.Tooltip */}
        {/*           align={'r'} */}
        {/*           trigger={ */}
        {/*             <Button */}
        {/*               size={'large'} */}
        {/*               onClick={() => { */}
        {/*                 if (rrd) { */}
        {/*                   setLoading(true); */}
        {/*                   RemoveRelayDirectories({ */}
        {/*                     root: rrd, */}
        {/*                   }).invoke((t) => { */}
        {/*                     setLoading(false); */}
        {/*                     if (!t.code) { */}
        {/*                       Message.success('Done'); */}
        {/*                     } */}
        {/*                   }); */}
        {/*                 } */}
        {/*               }} */}
        {/*             >{i18n.t('Start to remove')} */}
        {/*             </Button> */}
        {/*           } */}
        {/*         > */}
        {/*           &#123;{i18n.t('input path')}&#125;/aaa/bbb/* =&gt; &#123;{i18n.t('input path')}&#125;/aaa/* */}
        {/*         </Balloon.Tooltip> */}
        {/*       </div> */}
        {/*     </div> */}
        {/*     <div className={'tool'}> */}
        {/*       <Title title={'Extract sub directories recursively if directory contains folders only'} /> */}
        {/*       <div className="line"> */}
        {/*         <Input */}
        {/*           size={'large'} */}
        {/*           value={rrd} */}
        {/*           style={{ width: 800 }} */}
        {/*           placeholder={i18n.t('Root path')} */}
        {/*           onChange={(v) => setRrd(v)} */}
        {/*         /> */}
        {/*       </div> */}
        {/*       <div className="line"> */}
        {/*         <Balloon.Tooltip */}
        {/*           align={'r'} */}
        {/*           trigger={ */}
        {/*             <Button */}
        {/*               size={'large'} */}
        {/*               onClick={() => { */}
        {/*                 if (rrd) { */}
        {/*                   setLoading(true); */}
        {/*                   ExtraSubdirectories({ */}
        {/*                     model: { */}
        {/*                       path: rrd, */}
        {/*                     }, */}
        {/*                   }).invoke((t) => { */}
        {/*                     setLoading(false); */}
        {/*                     if (!t.code) { */}
        {/*                       Message.success('Done'); */}
        {/*                     } */}
        {/*                   }); */}
        {/*                 } */}
        {/*               }} */}
        {/*             >{i18n.t('Start to extract')} */}
        {/*             </Button> */}
        {/*           } */}
        {/*         > */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/aaa/xxx/111/*</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/aaa/yyy/222/*</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/aaa/zzz/333/*</div> */}
        {/*           <div>↓</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/xxx/111/*</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/yyy/222/*</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/zzz/333/*</div> */}
        {/*           <div>↓</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/111/*</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/222/*</div> */}
        {/*           <div>&#123;{i18n.t('input path')}&#125;/333/*</div> */}
        {/*           <div>-----------------------</div> */}
        {/*           <div>{i18n.t('Not work for folders which have folders and files')}</div> */}
        {/*         </Balloon.Tooltip> */}
        {/*       </div> */}
        {/*     </div> */}
        {/*     <div className={'tool'}> */}
        {/*       <Title title={'Group same name files into directories'} /> */}
        {/*       <div className="form"> */}
        {/*         <div className="line"> */}
        {/*           <Input */}
        {/*             size={'large'} */}
        {/*             value={gfd} */}
        {/*             style={{ width: 800 }} */}
        {/*             placeholder={i18n.t('Input root path')} */}
        {/*             onChange={(v) => setGfd(v)} */}
        {/*           /> */}
        {/*         </div> */}
        {/*         <div className="line"> */}
        {/*           <Balloon.Tooltip */}
        {/*             align={'r'} */}
        {/*             trigger={ */}
        {/*               <Button */}
        {/*                 size={'large'} */}
        {/*                 onClick={() => { */}
        {/*                   if (gfd) { */}
        {/*                     setLoading(true); */}
        {/*                     GroupFilesToDirectories({ */}
        {/*                       root: gfd, */}
        {/*                     }).invoke((t) => { */}
        {/*                       setLoading(false); */}
        {/*                       if (!t.code) { */}
        {/*                         Message.success('Done'); */}
        {/*                       } */}
        {/*                     }); */}
        {/*                   } */}
        {/*                 }} */}
        {/*               > */}
        {/*                 {i18n.t('Start to merge')} */}
        {/*               </Button> */}
        {/*             } */}
        {/*           > */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/aaa.jpg</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/aaa.mp4</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/bbb.jpg</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/bbb.mp4</div> */}
        {/*             <div>↓</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/aaa/aaa.jpg</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/aaa/aaa.mp4</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/bbb/bbb.jpg</div> */}
        {/*             <div>&#123;{i18n.t('input path')}&#125;/bbb/bbb.mp4</div> */}
        {/*           </Balloon.Tooltip> */}
        {/*         </div> */}
        {/*       </div> */}
        {/*     </div> */}
        {/*   </div> */}
        {/* </Tab.Item> */}
        {/* <Tab.Item title={i18n.t('File mover')} key={2}> */}
        {/*   <FileMover /> */}
        {/* </Tab.Item> */}
        <Tab.Item title={i18n.t('JavLibrary')} key={3}>
          <JavLibrary />
        </Tab.Item>
      </Tab>
    </div>
  );
};
