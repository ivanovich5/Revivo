const steps=[['01 / Primero, una copia','USB → otro disco','Se lee la memoria y se crea una copia en otro disco para trabajar sobre ella.'],['02 / Después, revisa','Tú eliges qué conservar','Explora lo encontrado. Una vista previa ayuda a revisar el contenido, pero no garantiza que esté completo.'],['03 / Guarda tu selección','En el destino que elijas','Elige varios archivos y guárdalos por tipo en una carpeta con el nombre que prefieras.']];
document.querySelectorAll('[data-step]').forEach(button=>button.addEventListener('click',()=>{const index=Number(button.dataset.step);document.querySelectorAll('[data-step]').forEach(b=>b.setAttribute('aria-pressed',String(b===button)));document.querySelector('#demo-title').textContent=steps[index][0];document.querySelector('#demo-count').textContent=steps[index][1];document.querySelector('#demo-detail').textContent=steps[index][2];document.querySelector('.demo-track span').style.width=`${(index+1)/3*100}%`;}));
if('IntersectionObserver' in window){const observer=new IntersectionObserver(entries=>entries.forEach(entry=>{if(entry.isIntersecting){entry.target.classList.remove('waiting');observer.unobserve(entry.target);}}),{threshold:.08});document.querySelectorAll('.principle,.sectionheading,.steps article,.download,.support').forEach(el=>{el.classList.add('reveal','waiting');observer.observe(el);});}
const url=window.REVIVO_CONFIG?.donationUrl;
if(url){try{const target=new URL(url);if(target.protocol==='https:'){const a=document.createElement('a');a.className='button';a.href=target.href;a.rel='noopener noreferrer';a.target='_blank';a.textContent='Apoyar a Revivo ↗';document.getElementById('donation').replaceChildren(a);}}catch{}}

const feedbackForm=document.querySelector('#feedback-form');
if(feedbackForm){
 const status=document.querySelector('#feedback-status');
 feedbackForm.addEventListener('submit',async event=>{
  event.preventDefault();if(!feedbackForm.reportValidity())return;
  const button=feedbackForm.querySelector('button[type="submit"]');button.disabled=true;status.textContent='Enviando tu comentario…';
  const controller=new AbortController();const timeout=setTimeout(()=>controller.abort(),20000);
  try{
   const payload=Object.fromEntries(new FormData(feedbackForm));
   const response=await fetch('/api/feedback',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload),signal:controller.signal});
   if(!response.ok){if(response.status===429)throw new Error('Has enviado varios comentarios. Espera unos minutos e inténtalo de nuevo.');throw new Error('No se pudo guardar el comentario. Revisa los campos e inténtalo más tarde.');}
   const result=await response.json();if(!result.ok)throw new Error('No recibimos la confirmación. Inténtalo más tarde.');
   status.textContent='Gracias. Tu comentario quedó guardado para revisión.';feedbackForm.reset();
  }catch(error){status.textContent=error.name==='AbortError'?'No pudimos confirmar el envío. Conservamos tu texto; espera un momento antes de volver a intentarlo.':error.message;}
  finally{clearTimeout(timeout);button.disabled=false;}
 });
}

// Back to top: native anchor keeps keyboard and reduced-motion support.
const backTop=document.querySelector('.back-top');
if(backTop){
 const updateBackTop=()=>{backTop.hidden=window.scrollY<400;};
 window.addEventListener('scroll',updateBackTop,{passive:true});
 window.addEventListener('pageshow',updateBackTop);
 updateBackTop();
}
