"""
door.py runs on the Raspberry Pi. Subscribes to its command topic, drives a
GPIO pin to release the lock relay, publishes events back to the broker.

WIRING (typical):
    Pi 5V ──┬── relay VCC
    Pi GND ─┴── relay GND
    Pi GPIO 17 ── relay IN

The relay's NO/COM contacts then pulse the door strike's 12V circuit. Use an
optoisolated relay so a fault in the lock circuit can't fry the Pi.

Online/offline state is published with retain=True so a subscriber that connects
later sees the device's last-known state without waiting for the next heartbeat.
"""

import json
import os
import time
import paho.mqtt.client as mqtt

try:
    from gpiozero import OutputDevice
    relay = OutputDevice(17, active_high=True, initial_value=False)
except ImportError:                # so you can run it on a laptop for testing
    class FakeRelay:
        def on(self):  print("[relay] ON")
        def off(self): print("[relay] OFF")
    relay = FakeRelay()

BROKER = os.getenv("MQTT_HOST", "localhost")
PORT   = int(os.getenv("MQTT_PORT", "1883"))
TOPIC  = os.getenv("DOOR_TOPIC", "home/door/front")  # this Pi's identity

def on_connect(client, _userdata, _flags, rc, _properties=None):
    print(f"connected rc={rc}")
    client.subscribe(f"{TOPIC}/cmd")
    client.publish(f"{TOPIC}/evt", json.dumps({"type": "online"}), retain=True)

def on_message(client, _userdata, msg):
    try:
        cmd = json.loads(msg.payload)
    except Exception:
        return
    if cmd.get("action") == "unlock":
        # Real systems would consult a local allowlist / hardware keypad here too.
        relay.on(); time.sleep(3); relay.off()
        client.publish(f"{TOPIC}/evt",
                       json.dumps({"type": "granted", "userId": cmd.get("userId")}))

if __name__ == "__main__":
    c = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=f"pi-{TOPIC.replace('/', '-')}")
    c.will_set(f"{TOPIC}/evt", json.dumps({"type": "offline"}), retain=True)  # last-will
    c.on_connect = on_connect
    c.on_message = on_message
    c.connect(BROKER, PORT, keepalive=30)
    c.loop_forever()
