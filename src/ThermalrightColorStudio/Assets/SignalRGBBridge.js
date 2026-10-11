// Color Studio SignalRGB bridge. GPL-3.0. No USB device is opened by this plugin.
export function Name() { return "Color Studio Bridge"; }
export function Publisher() { return "Color Studio"; }
export function Version() { return "0.3.3"; }
export function Type() { return "network"; }
export function SubdeviceController() { return false; }
export function Size() { return [40, 26]; }
export function ControllableParameters() { return []; }
const POSITIONS = [[4,1],[10,1],[20,5],[20,9],[39,10],[24,24],[39,25],[4,4],[6,6],[6,9],[4,11],[2,9],[2,6],[4,7],[10,4],[11,6],[11,9],[10,11],[8,9],[8,6],[10,7],[15,4],[17,6],[17,9],[15,11],[13,9],[13,6],[15,7],[26,6],[28,7],[28,10],[26,11],[25,10],[25,7],[26,8],[30,6],[32,7],[32,10],[30,11],[29,10],[29,7],[30,8],[35,6],[36,7],[36,10],[35,11],[33,10],[33,7],[35,8],[2,19],[4,20],[4,24],[2,25],[0,24],[0,20],[2,22],[8,19],[10,20],[10,24],[8,25],[6,24],[6,20],[8,22],[13,19],[15,20],[15,24],[13,25],[12,24],[12,20],[13,22],[19,19],[21,20],[21,24],[19,25],[17,24],[17,20],[19,22],[31,21],[32,22],[32,25],[31,25],[29,25],[29,22],[31,23],[35,21],[37,22],[37,25],[35,25],[34,25],[34,22],[35,23],[28,22],[28,25]];
let sequence = 0;
export function Initialize() {
    device.setName("Color Studio · PS120 Digital");
    device.setSize([40,26]);
    device.setControllableLeds(["Segment 0", "Segment 1", "Segment 2", "Segment 3", "Segment 4", "Segment 5", "Segment 6", "Segment 7", "Segment 8", "Segment 9", "Segment 10", "Segment 11", "Segment 12", "Segment 13", "Segment 14", "Segment 15", "Segment 16", "Segment 17", "Segment 18", "Segment 19", "Segment 20", "Segment 21", "Segment 22", "Segment 23", "Segment 24", "Segment 25", "Segment 26", "Segment 27", "Segment 28", "Segment 29", "Segment 30", "Segment 31", "Segment 32", "Segment 33", "Segment 34", "Segment 35", "Segment 36", "Segment 37", "Segment 38", "Segment 39", "Segment 40", "Segment 41", "Segment 42", "Segment 43", "Segment 44", "Segment 45", "Segment 46", "Segment 47", "Segment 48", "Segment 49", "Segment 50", "Segment 51", "Segment 52", "Segment 53", "Segment 54", "Segment 55", "Segment 56", "Segment 57", "Segment 58", "Segment 59", "Segment 60", "Segment 61", "Segment 62", "Segment 63", "Segment 64", "Segment 65", "Segment 66", "Segment 67", "Segment 68", "Segment 69", "Segment 70", "Segment 71", "Segment 72", "Segment 73", "Segment 74", "Segment 75", "Segment 76", "Segment 77", "Segment 78", "Segment 79", "Segment 80", "Segment 81", "Segment 82", "Segment 83", "Segment 84", "Segment 85", "Segment 86", "Segment 87", "Segment 88", "Segment 89", "Segment 90", "Segment 91", "Segment 92"], POSITIONS);
    device.addFeature("udp"); sequence = 0;
}
export function Render() {
    if (typeof controller === "undefined" || !controller || !controller.token || controller.token.length !== 32) return;
    const packet = [84,67,83,82,1];
    for(let i=0;i<32;i+=2) packet.push(parseInt(controller.token.substr(i,2),16));
    sequence = (sequence + 1) >>> 0;
    for(let i=0;i<4;i++) packet.push((sequence >>> (i*8)) & 255);
    for(const p of POSITIONS) {
        const c = device.color(p[0],p[1]);
        packet.push(Math.max(0,Math.min(255,Math.round(c[0]))),Math.max(0,Math.min(255,Math.round(c[1]))),Math.max(0,Math.min(255,Math.round(c[2]))));
    }
    udp.send("127.0.0.1", 18743, packet, 1);
}
export function Shutdown() {}
// A loopback-only network controller, discovered via Color Studio's local info endpoint.
export function DiscoveryService() {
    this.IconUrl = "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%2296%22%20height%3D%2296%22%20viewBox%3D%220%200%2096%2096%22%3E%3Crect%20width%3D%2296%22%20height%3D%2296%22%20rx%3D%2220%22%20fill%3D%22%23121c2d%22%2F%3E%3Ccircle%20cx%3D%2248%22%20cy%3D%2248%22%20r%3D%2234%22%20fill%3D%22none%22%20stroke%3D%22%239b7bff%22%20stroke-width%3D%227%22%2F%3E%3Cpath%20d%3D%22M47%2030H31V65H47M64%2030H52V47H64V65H52%22%20fill%3D%22none%22%20stroke%3D%22%234edbff%22%20stroke-width%3D%226%22%2F%3E%3C%2Fsvg%3E";
    this.MDns = [];
    this.lastPoll = 0; this.pending = false; this.state = "Starting discovery";
    this.getStatus = function() { return this.state; };
    this.forceDiscover = function() { this.pending = false; this.lastPoll = 0; this.Update(); };
    this.Initialize = function() { this.Update(); };
    this.Update = function() {
        if (this.pending || Date.now() - this.lastPoll < 2000) return;
        this.lastPoll = Date.now(); this.pending = true;
        const owner = this;
        try {
            const xhr = new XMLHttpRequest();
            xhr.onreadystatechange = function() {
                if (xhr.readyState !== 4) return;
                owner.pending = false; owner.state = "HTTP " + xhr.status;
                try {
                    if (xhr.status !== 200) return;
                    const info = JSON.parse(xhr.responseText);
                    if (info.version !== 1 || info.ledCount !== 93 || !/^[A-Fa-f0-9]{32}$/.test(info.token)) { owner.state = "Invalid receiver response"; return; }
                    let entry = service.getController("colorstudio-local-lf8");
                    if (!entry) {
                        service.addController(new BridgeController(info.token));
                        entry = service.getController("colorstudio-local-lf8");
                    }
                    const target = entry && (entry.obj || entry);
                    if (target && typeof target.update === "function") { target.update(info.token); owner.state = "Controller ready"; }
                    else { owner.state = "Controller registration pending"; }
                } catch (err) { owner.state = String(err); service.log(owner.state); }
            };
            xhr.onerror = function() { owner.pending = false; owner.state = "Local HTTP connection failed"; };
            xhr.open("GET", "http://127.0.0.1:18742/info", true); xhr.send(null);
        } catch (err) { this.pending = false; this.state = String(err); service.log(this.state); }
    };
    this.Shutdown = function() {};
    this.Discovered = function() {};
    this.Removal = function() {};
}
class BridgeController {
    constructor(token) {
        this.id = "colorstudio-local-lf8"; this.name = "Color Studio PS120 Digital";
        this.sku = "COLORSTUDIO-LF8"; this.image = "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%2296%22%20height%3D%2296%22%20viewBox%3D%220%200%2096%2096%22%3E%3Crect%20width%3D%2296%22%20height%3D%2296%22%20rx%3D%2220%22%20fill%3D%22%23121c2d%22%2F%3E%3Ccircle%20cx%3D%2248%22%20cy%3D%2248%22%20r%3D%2234%22%20fill%3D%22none%22%20stroke%3D%22%239b7bff%22%20stroke-width%3D%227%22%2F%3E%3Cpath%20d%3D%22M47%2030H31V65H47M64%2030H52V47H64V65H52%22%20fill%3D%22none%22%20stroke%3D%22%234edbff%22%20stroke-width%3D%226%22%2F%3E%3C%2Fsvg%3E";
        this.ip = "127.0.0.1"; this.port = 18742; this.token = token;
        this.initialized = false;
    }
    update(token) {
        const changed = this.token !== token; this.token = token;
        if (!this.initialized) {
            this.initialized = true;
            service.updateController(this); service.announceController(this);
            service.log("Color Studio loopback bridge ready");
        } else if (changed) {
            // Refresh the existing device; announcing again creates a second render instance.
            service.updateController(this);
            service.log("Color Studio receiver session refreshed");
        }
    }
}
