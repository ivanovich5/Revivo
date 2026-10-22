'use strict';
let page=0;
const byId=id=>document.getElementById(id);
const number=n=>Number(n||0).toLocaleString('es-MX');
const date=value=>value?new Date(value).toLocaleString('es-MX'):'Sin registro';
function el(tag,text,className){const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(className)n.className=className;return n;}
function showMessage(item){
 const box=el('article',undefined,'message');
 const labels={error:'Error',suggestion:'Sugerencia',experience:'Experiencia'};
 box.append(el('h3',labels[item.kind]||item.kind),el('p',`${date(item.created)} · Versión: ${item.version||'No indicada'} · Correo: ${item.email||'No indicado'}`,'meta'),el('p',item.message,'content'));
 const state=el('select');state.id='state-'+item.id;
 for(const [value,text] of [['nuevo','Nuevo'],['en_revision','En revisión'],['resuelto','Resuelto']]){const option=el('option',text);option.value=value;state.append(option);}state.value=item.status;
 const stateLabel=el('label','Estado');stateLabel.htmlFor=state.id;
 const notes=el('textarea');notes.id='notes-'+item.id;notes.maxLength=2000;notes.value=item.notes;
 const notesLabel=el('label','Notas privadas');notesLabel.htmlFor=notes.id;
 const save=el('button','Guardar seguimiento');const status=el('p');status.setAttribute('role','status');
 save.addEventListener('click',async()=>{save.disabled=true;status.textContent='Guardando…';try{
  const response=await fetch('/admin/update',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({id:item.id,status:state.value,notes:notes.value})});
  if(!response.ok)throw new Error();
  status.textContent='Seguimiento guardado. Pulsa Actualizar para renovar el resumen.';
 }catch{status.textContent='No se pudo guardar. Tu texto sigue aquí; vuelve a intentarlo.';}finally{save.disabled=false;}});
 box.append(stateLabel,state,notesLabel,notes,save,status);return box;
}
async function load(){
 byId('refresh').disabled=true;byId('notice').textContent='Actualizando…';
 try{
  const response=await fetch('/admin/data?page='+page);if(!response.ok)throw new Error();const d=await response.json();
  byId('visits').textContent=number(d.totals.visit);byId('downloads').textContent=number(d.totals.download);byId('pending').textContent=number((d.statuses.nuevo||0)+(d.statuses.en_revision||0));
  byId('since').textContent='Conteo activo desde '+date(d.started)+'.';
  byId('feedback').replaceChildren(...(d.feedback.length?d.feedback.map(showMessage):[el('p','No hay comentarios en esta página.')]));
  byId('page').textContent=`Página ${page+1} · ${number(d.total)} comentarios`;byId('previous').disabled=page===0;byId('next').disabled=(page+1)*50>=d.total;
  const days=new Map();for(const row of d.days){if(!days.has(row.day))days.set(row.day,{});days.get(row.day)[row.kind]=row.count;}
  byId('daily').replaceChildren(...Array.from(days,([day,counts])=>{const tr=el('tr');tr.append(el('td',day),el('td',number(counts.visit)),el('td',number(counts.download)));return tr;}));
  const m=d.maintenance;const low=d.disk.free/d.disk.total<.15;
  const lines=[el('p',`Espacio libre: ${(d.disk.free/1073741824).toFixed(1)} GB de ${(d.disk.total/1073741824).toFixed(1)} GB.`,low?'warning':'good')];
  if(m.error)lines.push(el('p',m.error,'warning'));
  if(m.checked){lines.push(el('p','Última revisión: '+date(m.checked)));if(Date.now()-Date.parse(m.checked)>36*3600000)lines.push(el('p','La revisión de mantenimiento está atrasada.','warning'));}
  if(m.backup)lines.push(el('p','Respaldo diario comprobado: '+date(m.backup)));
  if(m.offsite){lines.push(el('p','Copia externa confirmada: '+date(m.offsite)));if(Date.now()-Date.parse(m.offsite)>7*86400000)lines.push(el('p','Actualiza la copia externa desde esta computadora.','warning'));}else lines.push(el('p','Aún no hay copia externa confirmada.','warning'));
  for(const warning of m.warnings||[])lines.push(el('p',warning,'warning'));
  for(const [name,ok] of Object.entries(m.checks||{}))lines.push(el('p',`${name}: ${ok?'Correcto':'Revisar'}`,ok?'good':'warning'));
  byId('maintenance').replaceChildren(...lines);byId('notice').textContent='Información actualizada. Los cambios sin guardar se pierden al actualizar o cambiar de página.';
 }catch{byId('notice').textContent='No se pudo conectar. Abre de nuevo «Panel REVIVO» y pulsa Actualizar.';}
 finally{byId('refresh').disabled=false;}
}
byId('refresh').addEventListener('click',load);byId('previous').addEventListener('click',()=>{page--;load();});byId('next').addEventListener('click',()=>{page++;load();});load();
