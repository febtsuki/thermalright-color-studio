import QtQuick
import QtQuick.Controls
Item {
    anchors.fill: parent
    Column {
        anchors.margins: 20
        anchors.fill: parent
        spacing: 12
        Label { text: "Color Studio PS120 Digital"; color: "#eeeeee"; font.pixelSize: 20 }
        Label { text: "Start Color Studio and enable SignalRGB sync.\nColors use local UDP; real sensor numbers remain in Color Studio."; color: "#bbbbbb"; wrapMode: Text.Wrap; width: parent.width }
        Label { text: "Local receiver: 127.0.0.1:18742"; color: "#bbbbbb" }
        Label { text: typeof service !== "undefined" ? "Registered controllers: " + service.controllers.length : "Local bridge"; color: "#bbbbbb" }
        Label { id: discoveryStatus; text: "Discovering local receiver"; color: "#bbbbbb" }
        Button { text: "Connect local Color Studio"; onClicked: discovery.forceDiscover() }
        Timer { interval: 2500; running: true; repeat: true; onTriggered: { if (typeof discovery !== "undefined") discoveryStatus.text = discovery.getStatus(); } }

    }
}
