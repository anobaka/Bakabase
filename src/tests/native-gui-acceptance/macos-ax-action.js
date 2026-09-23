let result;
try {result=ownedAX(input,Math.min(12000,input.readBudgetMs===undefined?12000:input.readBudgetMs)).press();}
catch(error) {
  result={performed:false,errorStage:error&&error.safe?error.stage:'validate-control',
    diagnostic:error&&error.safe?{code:error.code,operation:error.operation,attribute:error.attribute,axError:error.axError,
      countKind:error.countKind,countValue:error.countValue}:
      {code:'DirectAXTreeUnavailable',operation:null,attribute:null,axError:null}};
}
JSON.stringify(result);
