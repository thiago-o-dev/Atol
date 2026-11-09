"""Offline check of BoatController.WaveOrbit: net horizontal travel must be ~0 after full cycles.
Runs a stationary and a sailing hull through a 3-component Gerstner-like swell (lattice displacement
D(x,t), evaluated at the hull's world XZ like the SampleSurface kernel) and prints the drift."""
import numpy as np
g=9.81
waves=[(60,1.2,0.0),(35,0.7,0.6),(18,0.35,-0.8)]  # wavelength, amplitude, heading(rad)
def lattice(p,t):
    d=np.zeros(2)
    for L,A,h in waves:
        k=2*np.pi/L; w=np.sqrt(g*k); dirv=np.array([np.cos(h),np.sin(h)])
        d+=-dirv*A*np.sin(k*dirv@p-w*t)   # Gerstner horizontal motion (~Q*A)
    return d
def run(speed,follow=0.6,omega=2.6,zeta=0.85,comp=1.0,lat=1.65,T=600,dt=0.02):
    pos=np.zeros(2); vel=np.array([speed,0.]); off=lattice(pos,0)*follow; ov=np.zeros(2)
    pos0=pos.copy(); cruise=np.array([speed,0.]); cx=0.0; maxo=0
    for i in range(int(T/dt)):
        t=i*dt
        tgt=lattice(pos,t)*follow
        a=(tgt-off)*omega**2-ov*2*zeta*omega
        ov+=a*dt; off+=ov*dt
        along=ov[0]; across=ov[1]
        drive=a+np.array([along*comp,across*(comp+lat)])
        drive=np.clip(drive,-12,12)
        acc=drive-np.array([0.0,lat*vel[1]])-0.0*vel   # lateral hull drag only; sail holds cruise speed
        vel=vel+acc*dt; vel[0]=speed+ (vel[0]-speed)*np.exp(-comp*dt)
        pos=pos+vel*dt
        maxo=max(maxo,np.linalg.norm(off))
    rel=pos-cruise*T
    return rel,maxo
for sp in (0.0,6.0):
    rel,mo=run(sp)
    print(f"speed {sp}: net drift vs ideal after 600s = {rel.round(2)} m, peak orbit {mo:.2f} m")
