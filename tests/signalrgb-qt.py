from pathlib import Path
import sys,os,json
os.environ['QT_QPA_PLATFORM']='offscreen'

from PySide6.QtCore import QUrl,qVersion
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QJSEngine,QQmlEngine,QQmlComponent
app=QGuiApplication([]);engine=QJSEngine()
engine.evaluate('''var sent=[], leds=[], positions=[], entries={}, count=0;
var controller=null;
var udp={send:function(ip,port,packet,endian){sent.push({ip:ip,port:port,data:packet,endian:endian});}};
var device={setName:function(){},setSize:function(){},setControllableLeds:function(n,p){leds=n;positions=p;},addFeature:function(n){if(n!=="udp")throw Error("feature");},color:function(x,y){return [x,y,201];}};
var service={getController:function(id){return entries[id] && entries[id].obj;},addController:function(c){entries[c.id]={obj:c};},updateController:function(){},announceController:function(){count++;},log:function(){}};
function XMLHttpRequest(){this.status=200;this.responseText=JSON.stringify({version:1,ledCount:93,token:'00112233445566778899AABBCCDDEEFF'});this.open=function(){};this.send=function(){this.readyState=4;if(this.onreadystatechange)this.onreadystatechange();};}
''')
path=(Path(__file__).resolve().parents[1]/'src/ThermalrightColorStudio/Assets/SignalRGBBridge.js')
mod=engine.importModule(str(path));assert not mod.isError(),mod.toString()
for name in ['Name','Version','Type','Publisher','SubdeviceController','Size','ControllableParameters']:
 v=mod.property(name).call();assert not v.isError(),v.toString()
svc=mod.property('DiscoveryService').callAsConstructor();assert not svc.isError(),svc.toString()
v=svc.property('Initialize').callWithInstance(svc);assert not v.isError(),v.toString()
engine.evaluate("controller=entries['colorstudio-local-lf8'].obj;")
engine.evaluate("entries['colorstudio-local-lf8'].obj.update('FFEEDDCCBBAA99887766554433221100');")
for name in ['Initialize','Render','Render','Shutdown']:
 v=mod.property(name).call();assert not v.isError(),v.toString()
result=engine.evaluate('JSON.stringify({sent:sent,leds:leds,positions:positions,count:count})').toString();data=json.loads(result)
assert len(data['leds'])==93 and len(data['positions'])==93 and data['count']==1
for i,s in enumerate(data['sent']):
 assert s['ip']=='127.0.0.1' and s['port']==18743 and len(s['data'])==304 and s['data'][21]==i+1
 for j,pos in enumerate(data['positions']):assert s['data'][25+3*j:28+3*j]==pos+[201]
qml=QQmlEngine();component=QQmlComponent(qml,QUrl.fromLocalFile(str(path.with_suffix('.qml'))))
assert not component.isError(),[e.toString() for e in component.errors()]
obj=component.create();assert obj is not None,[e.toString() for e in component.errors()]
print('PASS Qt '+qVersion()+' native module import, metadata scan, DiscoveryService construction/update, 93 LEDs, 304-byte RGB packets, sequence and QML component creation')


