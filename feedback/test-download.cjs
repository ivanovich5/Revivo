const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const main=fs.existsSync(__dirname+'/../dist/main.js')?__dirname+'/../dist/main.js':__dirname+'/../web/main.js';
const code='//'+fs.readFileSync(main,'utf8').split('// Aggregate page opens')[1];
function run(modifiers={},dnt=false){
 const events=[],timers=[],scrolls=[],hashes=[];let click;
 const sandbox={navigator:{doNotTrack:dnt?'1':'0'},window:{crypto:{randomUUID:()=> '11111111-1111-4111-8111-111111111111'}},crypto:{randomUUID:()=> '11111111-1111-4111-8111-111111111111'},fetch:(url,args)=>{events.push(JSON.parse(args.body));return Promise.resolve({ok:true});},document:{querySelectorAll:()=>[{addEventListener:(name,fn)=>click=fn}],querySelector:()=>({scrollIntoView:o=>scrolls.push(o)})},setTimeout:fn=>timers.push(fn),matchMedia:()=>({matches:false}),history:{replaceState:(a,b,c)=>hashes.push(c)}};
 vm.runInNewContext(code,sandbox);
 let prevented=false;click({...modifiers,preventDefault:()=>prevented=true});timers.forEach(fn=>fn());
 assert.equal(prevented,false,'Native download must not be canceled');
 assert.equal(events.length,dnt?0:2);
 assert.equal(scrolls.length,modifiers.ctrlKey?0:1);
 if(!modifiers.ctrlKey)assert.equal(hashes[0],'#apoyar');
}
run();run({ctrlKey:true});run({},true);console.log('Download preserved, support scroll and privacy preference verified.');
