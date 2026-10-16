# Hardware telemetry and compatibility

## Sources and capability handling

WinOptimizer combines documented Windows APIs with LibreHardwareMonitor (LHM) for sensors that Windows itself does not expose uniformly. CPU and GPU utilization/temperature, clock, fan, and voltage readings are included only if LHM reports a sensor with a supported sensor type and a finite value. A sensor's reported type/name/source accompanies the reading. A missing sensor is not treated as zero and is not evidence of failed cooling.

CPU load is computed from deltas of `GetSystemTimes`; physical/available/committed memory uses `GlobalMemoryStatusEx`; disk busy counters use the Windows Performance Data Helper API; fixed volumes use `DriveInfo`; process CPU is a delta of process CPU time against elapsed wall time. WMI/CIM provides descriptive CPU/GPU/drive model data and is best effort. Some WMI calls can be slow or denied; those fields can remain unavailable while other measurements work.

## Sensor states

- **Collecting:** no prior sample exists for a delta-based measurement.
- **Unavailable:** the source is unsupported, not exposed, inaccessible, or no matching sensor was found.
- **Measured zero:** a supported sensor reported zero. For fan RPM, the blades stay still at zero.
- **Measured value:** finite validated numeric value with unit and source.
- **Failure:** the source threw or returned invalid data; the error is logged locally, and the UI keeps the value unavailable.

LibreHardwareMonitor may require administrator access or vendor-specific support for some motherboard sensors. This app does not elevate to obtain them and does not write to hardware controllers. Virtual machines and laptops commonly expose fewer readings than desktops.

## Fan behavior

Fan animation uses the reported RPM, with motion scaled and visually capped for comfortable viewing; the number remains the real reading. Multiple detected fan readings are selectable. If there is no RPM sensor, the vector fan remains still and the screen explains that Windows/firmware did not expose a compatible reading. There is no fan control.

## GPU and storage limits

Integrated and discrete GPUs can expose different telemetry. Every metric is checked separately; a GPU model name does not imply that utilization, memory, temperature, clocks, or fan are measurable. Drive model and volume free space are descriptive only. SMART/health is not currently implemented; WinOptimizer does not infer drive health from a successful read.

CPU and GPU temperature values are sensor readings, not a universal overheating diagnosis. A single threshold is not appropriate for all processor models. Drive type classification and health status are not currently shown as verified metrics.

## Sensor library

The app references `LibreHardwareMonitorLib` 0.9.6, whose upstream project is distributed under MPL-2.0. Source and license notices are available at [the project repository](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) and [NuGet](https://www.nuget.org/packages/LibreHardwareMonitorLib/). Review notices and source obligations before distributing a release. Updates should be checked for maintained status, API changes, and license changes.

## Troubleshooting

1. Check Windows and vendor chipset/GPU drivers are current.
2. Confirm the hardware itself exposes a sensor to Windows; BIOS and vendor utilities may show different information.
3. Restart WinOptimizer after changing hardware or drivers.
4. Provide the Windows build, hardware model, displayed source/sensor label, and whether a vendor utility can read the value. Do not share serial numbers or full diagnostic dumps.

Do not disable security software, install untrusted drivers, or run random sensor scripts to make a reading appear.
